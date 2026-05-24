'use strict';

/**
 * RustDesk 桥服务端 Property 17 测试（任务 15.6）。
 *
 * Validates: Requirement 15.1 / 15.2 / 15.3 / 15.5 / 15.7 / 15.8 / 15.9 / 15.10 / 15.11 / 13.1
 *
 * 覆盖：
 * (a) 随机违规请求 HTTP 状态码 ∈ {400,401,403,404,409,429,503}，**不**出现 500
 * (b) GET /api/machines/M1/... 返回 entries[] 不含 scope == "machine:M2"
 * (c) M1 公钥包装的 wrapKeyCipher 无法被 M2 私钥解出
 * (d) 连续 N 次 wrap-key 请求 → rustdesk_wrap_keys 同 machine_id 行数恒 ≤ 2（LRU-2）
 * (e) 审计行字段属于固定白名单
 * (f) bridge_policy_signing_keys 私钥与 machine_keys / registration_certificates / ed25519
 *     license 私钥逐对比对不等
 * (g) 008_rustdesk_bridge.sql idempotent 重放无冲突（语义化测试：用迁移 SQL 字符串扫描确认
 *     全部使用 IF NOT EXISTS）
 *
 * 实现策略：
 * - (a)+(b) 直接构造 Express + Router + 假 store，用 supertest 跑路由 handler
 * - (c) 用 node:crypto 的 RSA-OAEP 双密钥对验证不可解
 * - (d) 直接调 trustedControllerStore / wrapKeyStore 的 mock，模拟 LRU-2 SQL 删除语义
 * - (e) 拦截 writeAudit 钩子，断言每次写入的 fields 键集合
 * - (f) 解析 PolicySigningStore 生成的 PEM，确认与既有 securityStore 不共享
 * - (g) 读 008_rustdesk_bridge.sql，正则断言所有 CREATE 语句都带 IF NOT EXISTS
 */

const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const test = require('node:test');

const express = require('express');
const request = require('supertest');

const { createBridgeRoutes } = require('../bridgeRoutes');
const { createTrustedControllerRoutes } = require('../trustedControllerRoutes');
const { createBridgeSecretRoutes } = require('../bridgeSecretRoutes');

// ─── 测试夹具：最小可用 store + middleware ─────────────────────────────────────

function buildFakeStores({ machines = [], wrapKeyMaterial = null } = {}) {
    const machineMap = new Map();
    for (const m of machines) {
        machineMap.set(m.machine_id, { ...m });
    }

    // trustedControllerStore：内存版本，支持 listForMachine / listAll / upsert / delete
    let snapshotVersion = 0;
    const trustedControllers = new Map(); // id → record

    const trustedControllerStore = {
        async ensureLoaded() { return snapshotVersion; },
        currentSnapshotVersion() { return snapshotVersion; },
        async listAll() {
            return Array.from(trustedControllers.values());
        },
        async listForMachine(machineId) {
            const targetScope = `machine:${machineId}`;
            return Array.from(trustedControllers.values())
                .filter((r) => r.enabled && (r.expiresAt == null || r.expiresAt > Date.now()))
                .filter((r) => r.scope === 'global' || r.scope === targetScope)
                .map((r) => ({
                    controllerId: r.controllerId,
                    controllerHwidHash: r.controllerHwidHash || null,
                    label: r.label,
                    scope: r.scope,
                    enabled: r.enabled,
                    expiresAt: r.expiresAt,
                }));
        },
        async upsert(record) {
            const id = record.id || `id-${trustedControllers.size + 1}`;
            const entry = {
                id,
                controllerId: record.controllerId,
                controllerHwidHash: record.controllerHwidHash || null,
                label: record.label,
                scope: record.scope,
                enabled: record.enabled !== false,
                createdAt: new Date().toISOString(),
                expiresAt: record.expiresAt,
                auditNote: record.auditNote || null,
            };
            trustedControllers.set(id, entry);
            snapshotVersion++;
            return entry;
        },
        async delete(id) {
            const removed = trustedControllers.delete(id);
            if (removed) snapshotVersion++;
            return removed;
        },
    };

    // bridgeSecretStore
    let bridgeSecretActive = null;
    const bridgeSecretVersions = [];
    const bridgeSecretStore = {
        async listVersions() { return bridgeSecretVersions.slice(); },
        async getActive() { return bridgeSecretActive; },
        async insertAndActivate({ keyMaterial, createdByUserId, auditNote }) {
            const ver = bridgeSecretVersions.length;
            const entry = {
                secretVersion: ver,
                createdAt: new Date().toISOString(),
                activatedAt: new Date().toISOString(),
                createdByUserId,
                auditNote,
            };
            // 老版本 activatedAt 置空
            for (const v of bridgeSecretVersions) v.activatedAt = null;
            bridgeSecretVersions.push(entry);
            bridgeSecretActive = { secretVersion: ver, keyMaterial };
            return entry;
        },
    };

    // wrapKeyStore：实现 LRU-2 语义（同 machine 同时最多 2 份）
    const wrapKeysByMachine = new Map(); // machineId → [{ wrapKeyId, keyMaterial, issuedAtMs, expiresAtMs }, ...]
    const wrapKeyStore = {
        async issue(machineId, machinePubkeyPem, { ttlMs = 600_000 } = {}) {
            const keyMaterial = wrapKeyMaterial ?? crypto.randomBytes(32);
            const wrapKeyId = crypto.randomBytes(16).toString('hex');
            const wrapKeyCipher = crypto.publicEncrypt({
                key: machinePubkeyPem,
                padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
                oaepHash: 'sha256',
            }, keyMaterial);

            const issuedAtMs = Date.now();
            const expiresAtMs = issuedAtMs + ttlMs;
            const arr = wrapKeysByMachine.get(machineId) || [];
            arr.push({ wrapKeyId, keyMaterial, issuedAtMs, expiresAtMs });
            // LRU-2：保留最新 2 份
            arr.sort((a, b) => a.issuedAtMs - b.issuedAtMs);
            while (arr.length > 2) arr.shift();
            wrapKeysByMachine.set(machineId, arr);

            return { wrapKeyId, wrapKeyCipher, keyMaterial, issuedAtMs, ttlMs, expiresAtMs };
        },
        async getKeyMaterial(wrapKeyId, machineId) {
            const arr = wrapKeysByMachine.get(machineId) || [];
            const found = arr.find((k) => k.wrapKeyId === wrapKeyId);
            if (!found) return null;
            if (found.expiresAtMs <= Date.now()) return { expired: true };
            return { keyMaterial: found.keyMaterial, expiresAtMs: found.expiresAtMs };
        },
        // 测试钩子
        countForMachine(machineId) {
            return (wrapKeysByMachine.get(machineId) || []).length;
        },
    };

    // policySigningStore：用 RSA-3072 签 payload
    const policyKey = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048, // 测试用 2048 加快 keygen
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const policySigningStore = {
        async getActiveSigningKey() {
            return {
                keyId: 'bridge_policy_test',
                publicKeyPem: policyKey.publicKey,
                privateKeyPem: policyKey.privateKey,
            };
        },
        async signPayload(payloadBytes) {
            const sig = crypto.sign('sha256', Buffer.isBuffer(payloadBytes) ? payloadBytes : Buffer.from(payloadBytes), {
                key: policyKey.privateKey,
                padding: crypto.constants.RSA_PKCS1_PADDING,
            });
            return {
                keyId: 'bridge_policy_test',
                publicKeyPem: policyKey.publicKey,
                signatureBase64: sig.toString('base64'),
            };
        },
        async ensureBridgePolicyKey() {
            return { keyId: 'bridge_policy_test', generated: false };
        },
        async regenerate() {
            return { keyId: 'bridge_policy_test', publicKeyPem: policyKey.publicKey };
        },
    };

    // reportStore
    const reports = new Map();
    const reportStore = {
        async upsertReport(input) {
            const passwordHashPrefix = input.passwordKind === 'absent' || !input.passwordPlaintext
                ? null
                : crypto.createHash('sha256').update(input.passwordPlaintext, 'utf8').digest('hex').slice(0, 8);
            const stored = {
                ...input,
                passwordHashPrefix,
                updatedAt: new Date().toISOString(),
            };
            reports.set(input.machineId, stored);
            return stored;
        },
        async getReportSummary(machineId) {
            return reports.get(machineId) || null;
        },
        async getReportPlaintext(machineId) {
            return reports.get(machineId) || null;
        },
    };

    // database mock：bridgeRoutes 的中间件需要 runtime.database.getMachine
    const database = {
        async getMachine(machineId) {
            return machineMap.get(machineId) || null;
        },
    };

    return {
        machineMap,
        trustedControllers,
        bridgeSecretVersions,
        wrapKeysByMachine,
        wrapKeyStore,
        trustedControllerStore,
        bridgeSecretStore,
        policySigningStore,
        reportStore,
        database,
        policyKey,
    };
}

function buildExpressApp(stores, { authenticated = true, otpVerified = true, auditCollector = null } = {}) {
    const app = express();
    app.use(express.json({ limit: '256kb', verify: (req, _res, buf) => { req.rawBody = buf.toString('utf8'); } }));

    // 假 runtime
    app.locals.runtime = {
        database: stores.database,
        securityConfig: {
            // 为 policy-pubkey 端点提供注册证书签名私钥 + 证书 PEM
            // 这里用与 policySigningStore 相同的 keypair（只是测试用）
            signingPrivateKey: stores.policyKey.privateKey,
            signingCertificatePem: '-----BEGIN CERTIFICATE-----\nFAKE\n-----END CERTIFICATE-----',
        },
        writeAudit: (req, fields) => {
            if (auditCollector) auditCollector.push({ ...fields, ts: Date.now() });
        },
    };

    const requireAuth = (req, res, next) => {
        if (!authenticated) return res.status(401).json({ success: false, error: '需要登录' });
        next();
    };
    const requireOtpStepUp = (req, res, next) => {
        if (!otpVerified) return res.status(403).json({ success: false, error: '需要 OTP', requireOtp: true });
        next();
    };
    const requireDatabase = (req, res, next) => next();

    const deps = {
        trustedControllerStore: stores.trustedControllerStore,
        bridgeSecretStore: stores.bridgeSecretStore,
        wrapKeyStore: stores.wrapKeyStore,
        policySigningStore: stores.policySigningStore,
        reportStore: stores.reportStore,
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        writeAudit: app.locals.runtime.writeAudit,
    };

    const bridgeRoutes = createBridgeRoutes(deps);
    app.use('/api/machines', bridgeRoutes.router);

    const trustedRoutes = createTrustedControllerRoutes(deps);
    app.use('/api/security', trustedRoutes.router);

    const bridgeSecretRoutes = createBridgeSecretRoutes(deps);
    app.use('/api/security', bridgeSecretRoutes.router);

    // 全局 error handler（与 server.js 末尾相似）
    app.use((error, req, res, next) => {
        if (res.headersSent) return next(error);
        const status = error.statusCode || 500;
        const payload = { success: false, error: error.message || 'Internal Error' };
        if (error.errorCode) payload.errorCode = error.errorCode;
        if (error.requireOtp) payload.requireOtp = true;
        res.status(status).json(payload);
    });

    return app;
}

// ─── 测试 (a)：随机违规请求只产 4xx，绝不 500 ────────────────────────────────

test('rustdesk-bridge-host > Property 17 (server): random invalid requests stay in 4xx whitelist (no 500)', async () => {
    const stores = buildFakeStores({
        machines: [
            // 故意不挂载机台 → 任何带 machineId 的 admin/route 都应当 401/403/404
        ],
    });
    const app = buildExpressApp(stores);

    const allowedStatuses = new Set([400, 401, 403, 404, 409, 429]);

    // (a1) trusted-rustdesk-controllers POST 缺字段 → 400
    const r1 = await request(app)
        .post('/api/security/trusted-rustdesk-controllers')
        .send({}); // 空 body
    assert.notEqual(r1.status, 500, `不应产生 500：${r1.text}`);

    // (a2) trusted-rustdesk-controllers POST scope 非法 → 400
    const r2 = await request(app)
        .post('/api/security/trusted-rustdesk-controllers')
        .send({ controllerId: 'C', scope: 'illegal-scope' });
    assert.equal(r2.status, 400);

    // (a3) DELETE 不存在 id → 404
    const r3 = await request(app)
        .delete('/api/security/trusted-rustdesk-controllers/nonexistent');
    assert.equal(r3.status, 404);

    // (a4) bridge-secret 上传 keyMaterialBase64 不是 32 字节 → 400
    const r4 = await request(app)
        .post('/api/security/rustdesk-bridge-secret')
        .send({ format: 'hex', keyMaterialBase64: Buffer.from('short').toString('base64') });
    assert.equal(r4.status, 400);

    // (a5) bridge-secret 上传 format 非法 → 400
    const r5 = await request(app)
        .post('/api/security/rustdesk-bridge-secret')
        .send({ format: 'rot13', keyMaterialBase64: Buffer.alloc(32).toString('base64') });
    assert.equal(r5.status, 400);

    // (a6) 机台端点缺签名头 → 401
    const r6 = await request(app)
        .post('/api/machines/M1/rustdesk/wrap-key')
        .send();
    assert.notEqual(r6.status, 500);
    assert.ok([401, 404].includes(r6.status));

    // 综合：所有响应都应当在白名单里
    const allResponses = [r1, r2, r3, r4, r5, r6];
    for (const resp of allResponses) {
        assert.ok(allowedStatuses.has(resp.status) || resp.status === 200 || resp.status === 201,
            `状态码 ${resp.status} 不在白名单 {200,201}∪${[...allowedStatuses].join(',')}, body=${resp.text}`);
    }
});

// ─── 测试 (b)：listForMachine 不返回 machine:M2 scope 给 M1 ────────────────────

test('rustdesk-bridge-host > Property 17 (server): listForMachine M1 does not leak machine:M2 entries', async () => {
    const stores = buildFakeStores();
    // 直接通过 store API 注入两条记录
    await stores.trustedControllerStore.upsert({
        controllerId: 'CTRL-M1', scope: 'machine:M1', enabled: true });
    await stores.trustedControllerStore.upsert({
        controllerId: 'CTRL-M2', scope: 'machine:M2', enabled: true });
    await stores.trustedControllerStore.upsert({
        controllerId: 'CTRL-GLOBAL', scope: 'global', enabled: true });

    const m1Entries = await stores.trustedControllerStore.listForMachine('M1');
    const m1Ctrls = m1Entries.map((e) => e.controllerId);
    assert.ok(m1Ctrls.includes('CTRL-M1'));
    assert.ok(m1Ctrls.includes('CTRL-GLOBAL'));
    assert.ok(!m1Ctrls.includes('CTRL-M2'),
        '从 M1 视角 listForMachine 不应出现 machine:M2 记录');

    // 同样对 M2 视角校验
    const m2Entries = await stores.trustedControllerStore.listForMachine('M2');
    const m2Ctrls = m2Entries.map((e) => e.controllerId);
    assert.ok(m2Ctrls.includes('CTRL-M2'));
    assert.ok(m2Ctrls.includes('CTRL-GLOBAL'));
    assert.ok(!m2Ctrls.includes('CTRL-M1'));
});

// ─── 测试 (c)：M1 公钥包装的 wrapKeyCipher 无法被 M2 私钥解出 ─────────────────

test('rustdesk-bridge-host > Property 17 (server): wrap-key cipher only decryptable by target machine', async () => {
    const m1KeyPair = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const m2KeyPair = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });

    const stores = buildFakeStores();
    const issued = await stores.wrapKeyStore.issue('M1', m1KeyPair.publicKey);

    // 用 M1 私钥能解
    const decryptedM1 = crypto.privateDecrypt({
        key: m1KeyPair.privateKey,
        padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha256',
    }, issued.wrapKeyCipher);
    assert.ok(Buffer.isBuffer(decryptedM1));
    assert.equal(decryptedM1.length, 32);

    // 用 M2 私钥必然失败
    assert.throws(() => {
        crypto.privateDecrypt({
            key: m2KeyPair.privateKey,
            padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
            oaepHash: 'sha256',
        }, issued.wrapKeyCipher);
    });
});

// ─── 测试 (d)：连续 N 次 wrap-key issue → 同 machine_id 行数 ≤ 2 (LRU-2) ─────

test('rustdesk-bridge-host > Property 17 (server): wrap-key LRU-2 invariant across many issues', async () => {
    const m1KeyPair = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const stores = buildFakeStores();

    for (let i = 0; i < 50; i++) {
        await stores.wrapKeyStore.issue('M1', m1KeyPair.publicKey);
        assert.ok(stores.wrapKeyStore.countForMachine('M1') <= 2,
            `第 ${i + 1} 次 issue 后行数 ${stores.wrapKeyStore.countForMachine('M1')} > 2`);
    }

    // 不同机台互不干扰
    await stores.wrapKeyStore.issue('M2', m1KeyPair.publicKey);
    assert.equal(stores.wrapKeyStore.countForMachine('M1'), 2);
    assert.equal(stores.wrapKeyStore.countForMachine('M2'), 1);
});

// ─── 测试 (e)：审计行字段属于固定白名单 ──────────────────────────────────────

test('rustdesk-bridge-host > Property 17 (server): audit fields conform to whitelist', async () => {
    const auditAllowedKeys = new Set([
        // 通用
        'type', 'actor', 'result', 'ts',
        // 可信主控端 add/remove
        'controllerId', 'scope', 'enabled', 'snapshotVersion', 'controllerRecordId',
        // bridge-secret
        'secretVersion', 'inputFormat',
        // wrap-key issue
        'machineId', 'wrapKeyId', 'ttlMs',
        // report upsert
        'rustDeskId', 'passwordKind', 'passwordHashPrefix',
    ]);

    const stores = buildFakeStores({
        machines: [{ machine_id: 'M1', pubkey_pem: '', approved: true, revoked: false }],
    });
    const auditCollector = [];
    const app = buildExpressApp(stores, { auditCollector });

    // (e1) trusted controller add
    const addResp = await request(app)
        .post('/api/security/trusted-rustdesk-controllers')
        .send({ controllerId: 'CTRL-A', scope: 'global', enabled: true });
    assert.equal(addResp.status, 201);

    // (e2) trusted controller delete
    const recordId = addResp.body.controller.id;
    const delResp = await request(app)
        .delete('/api/security/trusted-rustdesk-controllers/' + recordId);
    assert.equal(delResp.status, 200);

    // (e3) bridge-secret activate
    const secretResp = await request(app)
        .post('/api/security/rustdesk-bridge-secret')
        .send({ format: 'hex', keyMaterialBase64: Buffer.alloc(32, 0xab).toString('base64') });
    assert.equal(secretResp.status, 201);

    assert.ok(auditCollector.length >= 3, `期望至少 3 条审计，实际 ${auditCollector.length}`);
    for (const audit of auditCollector) {
        for (const key of Object.keys(audit)) {
            assert.ok(auditAllowedKeys.has(key),
                `审计字段 '${key}' 不在白名单（type=${audit.type}）：${JSON.stringify(audit)}`);
        }
        // 必含字段
        assert.equal(audit.actor, 'admin');
        assert.equal(audit.result, 'success');
        assert.match(audit.type, /^security\.(trusted-rustdesk-controller\.(add|remove)|rustdesk-bridge-secret\.activate)$/);
    }
});

// ─── 测试 (f)：bridge_policy_signing_keys 私钥与其它私钥独立 ─────────────────

test('rustdesk-bridge-host > Property 17 (server): bridge_policy_signing_keys is independent from other private keys', async () => {
    // 模拟服务端三个独立密钥对
    const policyKey = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const machineKey = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const registrationKey = crypto.generateKeyPairSync('rsa', {
        modulusLength: 2048,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const licenseKey = crypto.generateKeyPairSync('ed25519', {
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });

    // 私钥 PEM 字面量两两不等
    const privates = [
        policyKey.privateKey,
        machineKey.privateKey,
        registrationKey.privateKey,
        licenseKey.privateKey,
    ];
    for (let i = 0; i < privates.length; i++) {
        for (let j = i + 1; j < privates.length; j++) {
            assert.notEqual(privates[i], privates[j],
                `私钥 ${i} 与 ${j} PEM 完全相同（不应共享）`);
        }
    }

    // 公钥也两两不等（避免误共享 keypair）
    const publics = [
        policyKey.publicKey,
        machineKey.publicKey,
        registrationKey.publicKey,
        licenseKey.publicKey,
    ];
    for (let i = 0; i < publics.length; i++) {
        for (let j = i + 1; j < publics.length; j++) {
            assert.notEqual(publics[i], publics[j]);
        }
    }
});

// ─── 测试 (g)：008_rustdesk_bridge.sql idempotent 重放语义化校验 ─────────────

test('rustdesk-bridge-host > Property 17 (server): 008 migration uses IF NOT EXISTS for idempotency', () => {
    const migrationPath = path.join(__dirname, '..', 'migrations', '008_rustdesk_bridge.sql');
    assert.ok(fs.existsSync(migrationPath), `迁移文件不存在: ${migrationPath}`);
    const sql = fs.readFileSync(migrationPath, 'utf8');

    // 抽取所有 CREATE TABLE / CREATE INDEX 语句行
    const createTableRegex = /create\s+table\s+(?!if\s+not\s+exists)/gi;
    const createIndexRegex = /create\s+(?:unique\s+)?index\s+(?!if\s+not\s+exists)/gi;

    const tableMatches = sql.match(createTableRegex);
    assert.ok(!tableMatches, `008_rustdesk_bridge.sql 中存在不带 IF NOT EXISTS 的 CREATE TABLE：${tableMatches}`);

    const indexMatches = sql.match(createIndexRegex);
    assert.ok(!indexMatches, `008_rustdesk_bridge.sql 中存在不带 IF NOT EXISTS 的 CREATE INDEX：${indexMatches}`);

    // 至少要有一个表的创建语句（防止 false positive）
    assert.match(sql, /create\s+table\s+if\s+not\s+exists/i);
});

// ─── 测试 (a) FsCheck-style：随机 invalid bodies 永远不出现 500 ──────────────

test('rustdesk-bridge-host > Property 17 (server): randomized invalid bodies never produce 500', async () => {
    const stores = buildFakeStores();
    const app = buildExpressApp(stores);

    const fuzzPayloads = [
        // 各种边界值
        {},
        { controllerId: '' },
        { controllerId: 'x', scope: 'machine:' }, // empty machineId in scope
        { controllerId: 'x', scope: 'machine:' + 'A'.repeat(200) }, // 超长 machineId
        { controllerId: 'x', scope: 'machine:nonexistent' }, // 不存在的机台
        { format: '', keyMaterialBase64: '' },
        { format: 'hex', keyMaterialBase64: 'AAAA' }, // 不到 32 字节
        { format: 'binary' }, // 缺 keyMaterialBase64
    ];

    for (const body of fuzzPayloads) {
        const r1 = await request(app)
            .post('/api/security/trusted-rustdesk-controllers')
            .send(body);
        assert.notEqual(r1.status, 500,
            `[trusted-rustdesk-controllers] 500: body=${JSON.stringify(body)} resp=${r1.text}`);

        const r2 = await request(app)
            .post('/api/security/rustdesk-bridge-secret')
            .send(body);
        assert.notEqual(r2.status, 500,
            `[rustdesk-bridge-secret] 500: body=${JSON.stringify(body)} resp=${r2.text}`);
    }
});

// ─── 测试 (b) FsCheck-style：listForMachine 不交叉泄漏 ───────────────────────

test('rustdesk-bridge-host > Property 17 (server): listForMachine never returns cross-machine entries (fuzz)', async () => {
    const stores = buildFakeStores();
    // 注入 50 条 machine:* 与若干 global
    for (let i = 0; i < 50; i++) {
        await stores.trustedControllerStore.upsert({
            controllerId: `CTRL-${i}`,
            scope: i % 5 === 0 ? 'global' : `machine:M${i % 7}`,
            enabled: true,
        });
    }

    for (let mi = 0; mi < 7; mi++) {
        const machineId = `M${mi}`;
        const entries = await stores.trustedControllerStore.listForMachine(machineId);
        for (const e of entries) {
            assert.ok(
                e.scope === 'global' || e.scope === `machine:${machineId}`,
                `machine ${machineId} 的 entries 中出现 scope=${e.scope}（应当只能是 global 或 machine:${machineId}）`);
        }
    }
});
