/**
 * 缺陷条件探索测试 — 部署安全漏洞利用
 *
 * **Validates: Requirements 1.1, 1.5, 1.6, 1.7, 1.17, 1.18, 1.19, 1.30**
 *
 * 此测试编码了期望行为（修复后应通过）。
 * 在未修复代码上运行时，测试应 FAIL —— 失败即确认缺陷存在。
 *
 * 攻击面覆盖：
 * - A: 路径遍历（packageId 含 ../ 通过校验）
 * - C: Token 重用（已消费 token 仍通过验证）
 * - F: 资源边界（超大 errorMessage / records / sigFile 被接受）
 * - I: 状态机（非法状态转换被接受）
 */
const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');
const { createApp } = require('../server');

// ========== Test Harness ==========

function createFakeDatabase() {
    const machines = new Map();
    const packages = new Map();
    const tasks = new Map();
    const tokens = new Map();
    const records = new Map();

    return {
        async initialize() { },
        async close() { },
        async getMachine(machineId) { return machines.get(machineId) || null; },
        async upsertMachine(machineId, isProtected, vhdKeyword) {
            const machine = {
                machine_id: machineId, protected: isProtected ?? false,
                vhd_keyword: vhdKeyword ?? 'SDEZ', approved: false,
                evhd_password_configured: false, pubkey_pem: null, revoked: false,
            };
            machines.set(machineId, machine);
            return machine;
        },
        async updateMachineKey(machineId, { keyId, keyType, pubkeyPem }) {
            const machine = machines.get(machineId);
            if (machine) { machine.key_id = keyId; machine.key_type = keyType; machine.pubkey_pem = pubkeyPem; }
            return machine || null;
        },
        async updateMachineLastSeen() { return new Date().toISOString(); },
        async query(sql, params) {
            if (sql.includes('INSERT INTO deployment_packages')) {
                const pkg = { package_id: params[0], name: params[1], version: params[2], type: params[3], signer: params[4], file_path: params[5], file_size: params[6], created_at: new Date().toISOString(), expires_at: params[7] };
                packages.set(params[0], pkg);
                return { rows: [pkg] };
            }
            if (sql.includes('SELECT * FROM deployment_packages WHERE package_id')) {
                return { rows: [packages.get(params[0])].filter(Boolean) };
            }
            if (sql.includes('INSERT INTO deployment_tasks')) {
                const task = { task_id: params[0], package_id: params[1], machine_id: params[2], task_type: params[3] || 'deploy', status: 'pending', scheduled_at: params[4], started_at: null, completed_at: null, lease_expires_at: null, error_message: null, created_at: new Date().toISOString() };
                tasks.set(params[0], task);
                return { rows: [task] };
            }
            if (sql.includes('SELECT * FROM deployment_tasks WHERE task_id')) {
                return { rows: [tasks.get(params[0])].filter(Boolean) };
            }
            if (sql.includes('UPDATE deployment_tasks')) {
                const task = tasks.get(params[0]);
                if (task) {
                    task.status = params[1] || task.status;
                    if (sql.includes('completed_at = NOW()')) task.completed_at = new Date().toISOString();
                    if (sql.includes('lease_expires_at = NULL')) task.lease_expires_at = null;
                    if (params.length > 2) {
                        const lastParam = params[params.length - 1];
                        if (typeof lastParam === 'string' && !Number.isFinite(Number(lastParam)) && lastParam !== task.status) {
                            task.error_message = lastParam;
                        }
                    }
                }
                return { rows: [task].filter(Boolean) };
            }
            if (sql.includes('INSERT INTO deployment_tokens')) {
                const token = { token: params[0], task_id: params[1], machine_id: params[2], package_id: params[3], resource_type: params[4], expires_at: new Date(Date.now() + 60 * 60 * 1000).toISOString(), aes_key: params[5] || null, aes_iv: params[6] || null, used_at: null };
                tokens.set(params[0], token);
                return { rows: [token] };
            }
            if (sql.includes('deployment_tokens') && sql.includes('token = $1') && sql.includes('SELECT')) {
                const token = tokens.get(params[0]);
                if (token && token.machine_id === params[1] && token.package_id === params[2] && token.resource_type === params[3]) {
                    return { rows: [token] };
                }
                return { rows: [] };
            }
            if (sql.includes('UPDATE deployment_tokens SET used_at')) {
                const token = tokens.get(params[0]);
                if (token) token.used_at = new Date().toISOString();
                return { rowCount: token ? 1 : 0 };
            }
            if (sql.includes('INSERT INTO deployment_records')) {
                const record = { record_id: params[0], machine_id: params[1], package_id: params[2], name: params[3], version: params[4], type: params[5], target_path: params[6], status: params[7], deployed_at: params[8], uninstalled_at: params[9] };
                records.set(params[0], record);
                return { rows: [record] };
            }
            if (sql.includes('SELECT * FROM deployment_records WHERE machine_id')) {
                return { rows: Array.from(records.values()).filter(r => r.machine_id === params[0]) };
            }
            if (sql.includes('SELECT * FROM deployment_records WHERE record_id')) {
                return { rows: [records.get(params[0])].filter(Boolean) };
            }
            if (sql.includes('SELECT * FROM deployment_packages ORDER BY created_at')) {
                return { rows: Array.from(packages.values()) };
            }
            if (sql.includes('DELETE FROM deployment_tasks WHERE package_id')) {
                return { rows: [] };
            }
            if (sql.includes('DELETE FROM deployment_packages WHERE package_id')) {
                const pkg = packages.get(params[0]);
                packages.delete(params[0]);
                return { rows: pkg ? [pkg] : [] };
            }
            return { rows: [] };
        },
        async withClient(work) { return work(this); },
        async withTransaction(work) { return work(this); },
        async getMachineLogRuntimeSettings() { return { defaultRetentionActiveDays: 7, dailyInspectionHour: 3, dailyInspectionMinute: 0, timezone: 'UTC', lastInspectionAt: null }; },
        async updateMachineLogRuntimeSettings() { },
    };
}

function buildMachineRequestSigningPayload({ machineId, keyId, method, path, host = '127.0.0.1', timestamp, nonce, body = '' }) {
    const bodyHash = crypto.createHash('sha256').update(body, 'utf8').digest('hex');
    return ['VHDMountDeploymentRequestV1', String(machineId || '').trim(), String(keyId || '').trim(), String(method || '').trim().toUpperCase(), String(path || '').trim(), String(host || '').trim(), String(timestamp || '').trim(), String(nonce || '').trim(), bodyHash].join('\n');
}

function createMachineAuthHeaders({ privateKey, machineId, keyId, method, path, host = '127.0.0.1', body = '' }) {
    const timestamp = String(Date.now());
    const nonce = crypto.randomBytes(16).toString('hex');
    const payload = buildMachineRequestSigningPayload({ machineId, keyId, method, path, host, timestamp, nonce, body });
    const signature = crypto.sign('RSA-SHA256', Buffer.from(payload, 'utf8'), privateKey).toString('base64');
    return { 'X-VHDM-KeyId': keyId, 'X-VHDM-Timestamp': timestamp, 'X-VHDM-Nonce': nonce, 'X-VHDM-Signature': signature };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-security-bug-'));
    t.after(() => { fs.rmSync(tempDir, { recursive: true, force: true }); });

    const fakeDatabase = createFakeDatabase();
    const { app, runtime } = await createApp({
        logger: { log: () => {}, error: () => {}, info: () => {} },
        database: fakeDatabase,
        configDir: tempDir,
        disableSignalHandlers: true,
    });

    const client = request.agent(app);
    const prepareResponse = await client.post('/api/init/prepare').send({ issuer: 'VHDMountTest', accountName: 'admin' }).expect(201);
    const totpSecret = prepareResponse.body.totpSecret;
    await client.post('/api/init/complete').send({
        adminPassword: 'ComplexPassword123!',
        sessionSecret: '0123456789abcdef0123456789abcdef',
        totpCode: authenticator.generate(totpSecret),
        dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
        defaultVhdKeyword: 'SAFEBOOT',
    }).expect(201);

    const testKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const machine = await fakeDatabase.upsertMachine('machine-001', false, 'SDEZ');
    machine.approved = true;
    await fakeDatabase.updateMachineKey('machine-001', {
        keyId: 'test-key-001',
        keyType: 'RSA',
        pubkeyPem: testKeyPair.publicKey.export({ type: 'spki', format: 'pem' }),
    });

    return { app, runtime, tempDir, database: fakeDatabase, testKeyPair, testKeyId: 'test-key-001', totpSecret };
}

async function loginAndVerifyOtp(client, app) {
    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    const runtime = app.locals.runtime;
    const secret = runtime.securityConfig.totpSecret;
    await client.post('/api/auth/otp/verify').send({ code: authenticator.generate(secret) }).expect(200);
}

// ========== 测试 A - 路径遍历：packageId 含 ../ 应被拒绝 ==========

test('测试 A - 路径遍历: 包含 ../ 的 packageId 应被拒绝 (400)', async (t) => {
    // **Validates: Requirements 1.1**
    // 期望行为：packageId 不匹配 ^pkg-[a-f0-9]{32}$ 时应返回 400
    // 当前缺陷：assertString 仅检查长度，允许 ../../../etc/passwd.sig 通过
    const { app } = await createInitializedHarness(t);

    const maliciousIds = [
        '../../../etc/passwd.sig',
        '../../secret/key',
        'pkg-../../../etc/shadow',
        'anything-that-is-not-pkg-hex32',
    ];

    for (const maliciousId of maliciousIds) {
        const res = await request(app)
            .get(`/api/deployments/packages/${encodeURIComponent(maliciousId)}/signature?token=test&machineId=m1`)
            .set('User-Agent', 'VHDMount/1.0.0');

        // 期望：对无效 packageId 格式立即返回 400（而非 403/404 等后续逻辑）
        assert.strictEqual(res.status, 400,
            `packageId "${maliciousId}" 应被格式校验拒绝(400)，实际返回 ${res.status}`);
    }
});

// ========== 测试 C - Token 重用：已消费 token 应被拒绝 ==========

test('测试 C - Token 重用: 已消费的 token (used_at 非 NULL) 应返回 403', async (t) => {
    // **Validates: Requirements 1.6, 1.7**
    // 期望行为：validateToken 应检查 used_at IS NULL，已使用 token 返回 403
    // 当前缺陷：validateToken 的 SQL 不含 used_at IS NULL 条件
    const { app, database, tempDir } = await createInitializedHarness(t);

    // 创建一个包和对应的文件
    const pkgId = 'pkg-' + crypto.randomBytes(16).toString('hex');
    const filePath = path.join(tempDir, 'deployment-packages', `${pkgId}.zip`);
    const sigPath = path.join(tempDir, 'deployment-packages', `${pkgId}.zip.sig`);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, crypto.randomBytes(64));
    fs.writeFileSync(sigPath, crypto.randomBytes(32));

    await database.query(
        'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        [pkgId, 'TestPkg', '1.0.0', 'software-deploy', 'test', filePath, 64, new Date(Date.now() + 86400000).toISOString()]
    );

    // 创建一个 token 并手动标记为已使用
    const tokenValue = crypto.randomBytes(32).toString('hex');
    await database.query(
        'INSERT INTO deployment_tokens (token, task_id, machine_id, package_id, resource_type, expires_at, aes_key, aes_iv) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        [tokenValue, 'task-001', 'machine-001', pkgId, 'package', new Date(Date.now() + 3600000).toISOString(), null, null]
    );

    // 标记 token 为已使用
    await database.query('UPDATE deployment_tokens SET used_at', [tokenValue]);

    // 尝试使用已消费的 token 下载
    const res = await request(app)
        .get(`/api/deployments/packages/${pkgId}/download?token=${tokenValue}&machineId=machine-001`)
        .set('User-Agent', 'VHDMount/1.0.0');

    // 期望：已使用的 token 应返回 403
    assert.strictEqual(res.status, 403,
        `已消费的 token 应被拒绝(403)，实际返回 ${res.status}`);
});

// ========== 测试 F - 资源边界：超大 errorMessage 应被拒绝 ==========

test('测试 F - 资源边界: 超过 4096 字符的 errorMessage 应被拒绝', async (t) => {
    // **Validates: Requirements 1.17**
    // 期望行为：errorMessage 长度 > 4096 时返回 400
    // 当前缺陷：reportTaskStatus 中 errorMessage 无针对性长度校验
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    // 创建任务
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-err-001', 'pkg-001', 'machine-001', 'deploy', 'running', null]
    );

    const longErrorMessage = 'x'.repeat(5000); // > 4096 字符
    const statusPath = '/api/machines/machine-001/deployments/task-err-001/status';
    const body = JSON.stringify({ status: 'failed', errorMessage: longErrorMessage });

    const res = await request(app)
        .post(statusPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: statusPath,
            body,
        }))
        .send(body);

    // 期望：超长 errorMessage 应返回 400
    assert.strictEqual(res.status, 400,
        `超过 4096 字符的 errorMessage 应被拒绝(400)，实际返回 ${res.status}`);
});

// ========== 测试 F - 资源边界：超过 1000 条的 records 应被拒绝 ==========

test('测试 F - 资源边界: 超过 1000 条的 syncRecords 应被拒绝', async (t) => {
    // **Validates: Requirements 1.18**
    // 期望行为：records.length > 1000 时返回 413
    // 当前缺陷：syncRecords 无显式条目数限制
    const { app, testKeyPair, testKeyId } = await createInitializedHarness(t);

    const tooManyRecords = Array.from({ length: 1001 }, (_, i) => ({
        recordId: `rec-${String(i).padStart(4, '0')}`,
        packageId: 'pkg-' + crypto.randomBytes(16).toString('hex'),
        name: `Pkg${i}`,
        version: '1.0.0',
        type: 'software-deploy',
        status: 'success',
        deployedAt: new Date().toISOString(),
    }));

    const syncPath = '/api/machines/machine-001/deployments/sync';
    const body = JSON.stringify({ records: tooManyRecords });

    const res = await request(app)
        .post(syncPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: syncPath,
            body,
        }))
        .send(body);

    // 期望：超过 1000 条记录应返回 413
    assert.ok(res.status === 413 || res.status === 400,
        `超过 1000 条 records 应被拒绝(413 或 400)，实际返回 ${res.status}`);
});

// ========== 测试 F - 资源边界：超过 1MB 的 sigFile 应被拒绝 ==========

test('测试 F - 资源边界: 超过 1MB 的签名文件应被拒绝', async (t) => {
    // **Validates: Requirements 1.19**
    // 期望行为：sigFile.size > 1MB 时返回 413
    // 当前缺陷：签名文件仅受全局 2GB fileUpload 限制
    const { app, tempDir } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const pkgPath = path.join(tempDir, 'test-pkg.zip');
    fs.writeFileSync(pkgPath, crypto.randomBytes(32));

    // 创建一个 > 1MB 的签名文件
    const largeSigPath = path.join(tempDir, 'large-sig.sig');
    fs.writeFileSync(largeSigPath, crypto.randomBytes(1.5 * 1024 * 1024)); // 1.5MB

    const res = await client
        .post('/api/deployments/packages')
        .field('name', 'TestPkg')
        .field('version', '1.0.0')
        .field('type', 'software-deploy')
        .field('signer', 'admin')
        .attach('package', pkgPath)
        .attach('signature', largeSigPath);

    // 期望：超过 1MB 的签名文件应返回 413
    assert.ok(res.status === 413 || res.status === 400,
        `超过 1MB 的签名文件应被拒绝(413 或 400)，实际返回 ${res.status}`);
});

// ========== 测试 I - 状态机：非法状态转换应被拒绝 ==========

test('测试 I - 状态机: pending→success 非法转换应返回 409', async (t) => {
    // **Validates: Requirements 1.30**
    // 期望行为：非法状态转换返回 409 Conflict
    // 当前缺陷：updateTaskStatus 不校验状态转换合法性
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    // 创建一个 pending 状态的任务
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-sm-001', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );

    // 尝试直接从 pending 跳到 success（非法：应该是 pending→downloading→running→success）
    const statusPath = '/api/machines/machine-001/deployments/task-sm-001/status';
    const body = JSON.stringify({ status: 'success' });

    const res = await request(app)
        .post(statusPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: statusPath,
            body,
        }))
        .send(body);

    // 期望：非法状态转换应返回 409
    assert.strictEqual(res.status, 409,
        `pending→success 非法转换应返回 409，实际返回 ${res.status}`);
});

test('测试 I - 状态机: running→downloading 非法转换应返回 409', async (t) => {
    // **Validates: Requirements 1.30**
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-sm-002', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );
    // 手动设置为 running
    const task = (await database.query('SELECT * FROM deployment_tasks WHERE task_id', ['task-sm-002'])).rows[0];
    task.status = 'running';

    const statusPath = '/api/machines/machine-001/deployments/task-sm-002/status';
    const body = JSON.stringify({ status: 'downloading' });

    const res = await request(app)
        .post(statusPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: statusPath,
            body,
        }))
        .send(body);

    // 期望：running→downloading 非法转换应返回 409
    assert.strictEqual(res.status, 409,
        `running→downloading 非法转换应返回 409，实际返回 ${res.status}`);
});
