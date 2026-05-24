'use strict';

/**
 * RustDesk 桥机台端 5 个端点（任务 15.1 / Requirement 15.1 / 15.6 / 15.10 / 15.11 / 13.1）。
 *
 *  - POST /api/machines/:machineId/rustdesk/report
 *  - GET  /api/machines/:machineId/rustdesk/trusted-controllers
 *  - POST /api/machines/:machineId/rustdesk/wrap-key
 *  - GET  /api/machines/:machineId/rustdesk/policy-pubkey
 *  - GET  /api/machines/:machineId/rustdesk/bridge-secret
 *
 * 每条端点都通过 §6.8 的 4 头 RSA-PKCS1-SHA256 签名校验机台身份。本文件实现的
 * requireBridgeMachineSignature 中间件覆盖以下 5 个 payload 字面量白名单：
 *  - VHDMounterRustDeskReportV1
 *  - VHDMounterTrustedControllersFetchV1
 *  - VHDMounterWrapKeyV1
 *  - VHDMounterPolicyPubkeyFetchV1
 *  - VHDMounterBridgeSecretFetchV1
 *
 * 速率限制（per machineId 内存窗口）：
 *  - /trusted-controllers: 30s 一次
 *  - /wrap-key: 6 次/分钟
 *  - /bridge-secret: 30s 一次
 *
 * 错误码映射严格按 design.md：
 *  - 400 + WRAP_KEY_EXPIRED：wrapKey 不在有效集合
 *  - 400 + PAYLOAD_AAD_MISMATCH：AES-GCM AAD 校验失败
 *  - 401 / 403 / 404 / 409 / 429 / 503 按 §15.1
 *
 * 所有写操作走 runtime.writeAudit；审计行字段严格按 §15.7 / §15.8 / §15.9 白名单。
 *
 * 工厂函数 `createBridgeRoutes(deps)` 返回 `{ router, requireBridgeMachineSignature }`。
 * 不在本文件挂载到 server.js（任务 15.5 是 Wave 4）。
 */

const crypto = require('crypto');
const express = require('express');
const http = require('node:http');

const SIGNATURE_WINDOW_MS = 5 * 60 * 1000;
const NONCE_TTL_MS = 6 * 60 * 1000;

const ALLOWED_PAYLOAD_VERSIONS = Object.freeze({
    'POST /api/machines/:machineId/rustdesk/report': 'VHDMounterRustDeskReportV1',
    'GET /api/machines/:machineId/rustdesk/trusted-controllers': 'VHDMounterTrustedControllersFetchV1',
    'POST /api/machines/:machineId/rustdesk/wrap-key': 'VHDMounterWrapKeyV1',
    'GET /api/machines/:machineId/rustdesk/policy-pubkey': 'VHDMounterPolicyPubkeyFetchV1',
    'GET /api/machines/:machineId/rustdesk/bridge-secret': 'VHDMounterBridgeSecretFetchV1',
});

const PASSWORD_KIND_ALLOWED = new Set(['temporary', 'permanent', 'preset', 'absent']);
const REPORT_REASON_ALLOWED = new Set(['startup', 'id_change', 'password_change', 'rotation', 'heartbeat']);

const TTL_WRAP_KEY_MS = 600_000; // 与 wrapKeyStore 默认一致

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function asyncHandler(handler) {
    return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
}

function buildBodyHashHex(req) {
    if (typeof req.rawBody === 'string') {
        return crypto.createHash('sha256').update(req.rawBody, 'utf8').digest('hex');
    }
    if (req.body != null && Object.keys(req.body).length > 0) {
        return crypto.createHash('sha256').update(JSON.stringify(req.body), 'utf8').digest('hex');
    }
    return crypto.createHash('sha256').update('', 'utf8').digest('hex');
}

function buildSigningPayload({ payloadVersion, machineId, keyId, method, path, host, timestamp, nonce, bodyHash }) {
    return [
        payloadVersion,
        String(machineId || '').trim(),
        String(keyId || '').trim(),
        String(method || '').toUpperCase(),
        String(path || ''),
        String(host || '').split(':')[0],
        String(timestamp || ''),
        String(nonce || ''),
        String(bodyHash || '').toLowerCase(),
    ].join('\n');
}

/**
 * 这里手工挑出 expressPath 模板（如 "/api/machines/:machineId/rustdesk/report"）+ method 来定位
 * payloadVersion 白名单。匹配逻辑在中间件挂载路由时由调用方传入 routeKey。
 */
function makeRequireBridgeMachineSignature(routeKey) {
    if (!ALLOWED_PAYLOAD_VERSIONS[routeKey]) {
        throw new Error(`requireBridgeMachineSignature: 未知 routeKey=${routeKey}`);
    }
    const payloadVersion = ALLOWED_PAYLOAD_VERSIONS[routeKey];

    return async function requireBridgeMachineSignature(req, res, next) {
        try {
            const runtime = req.app.locals.runtime;
            const machineId = String(req.params.machineId || '').trim();
            if (!machineId || machineId.length > 64) {
                throw createJsonError(400, 'machineId 不能为空且长度不超过 64');
            }

            const machine = await runtime.database.getMachine(machineId);
            if (!machine) {
                throw createJsonError(404, '机台不存在');
            }
            if (!machine.pubkey_pem) {
                throw createJsonError(400, '机台未注册公钥');
            }
            if (machine.revoked) {
                throw createJsonError(403, '机台密钥已吊销');
            }
            if (!machine.approved) {
                throw createJsonError(403, '机台密钥未审批');
            }

            const keyId = String(req.get('x-vhdm-keyid') || '').trim();
            const timestampRaw = String(req.get('x-vhdm-timestamp') || '').trim();
            const nonce = String(req.get('x-vhdm-nonce') || '').trim();
            const signatureBase64 = String(req.get('x-vhdm-signature') || '').trim();
            if (!keyId || !timestampRaw || !nonce || !signatureBase64) {
                throw createJsonError(401, '需要机台签名认证');
            }
            if (machine.key_id && keyId !== machine.key_id) {
                throw createJsonError(403, '机台 keyId 不匹配');
            }

            const timestamp = Number.parseInt(timestampRaw, 10);
            if (!Number.isFinite(timestamp)) {
                throw createJsonError(400, '机台签名时间戳无效');
            }
            if (Math.abs(Date.now() - timestamp) > SIGNATURE_WINDOW_MS) {
                throw createJsonError(401, '机台签名已过期');
            }

            // 与 deploymentRoutes 共享同一个 nonce cache（runtime.deploymentRequestNonceCache）
            // —— 已经在 server.js 中初始化为 Map；不会污染部署链路（nonce 是签名 + 时间戳唯一组合）
            const nonceCache = runtime.deploymentRequestNonceCache;
            const cutoff = Date.now() - NONCE_TTL_MS;
            for (const [k, ts] of nonceCache.entries()) {
                if (Number(ts) < cutoff) nonceCache.delete(k);
            }
            const nonceKey = `bridge:${machineId}:${nonce}`;
            if (nonceCache.has(nonceKey)) {
                throw createJsonError(409, '机台签名 nonce 重复');
            }
            nonceCache.set(nonceKey, Date.now());

            const bodyHash = buildBodyHashHex(req);
            const requestHost = (req.get('host') || '').split(':')[0];
            const payload = buildSigningPayload({
                payloadVersion,
                machineId,
                keyId,
                method: req.method,
                path: req.path,
                host: requestHost,
                timestamp,
                nonce,
                bodyHash,
            });

            let signatureBytes;
            try {
                signatureBytes = Buffer.from(signatureBase64, 'base64');
            } catch {
                throw createJsonError(400, '机台签名格式无效');
            }

            const verifier = crypto.createVerify('RSA-SHA256');
            verifier.update(payload, 'utf8');
            verifier.end();
            if (!verifier.verify(machine.pubkey_pem, signatureBytes)) {
                throw createJsonError(401, '机台签名校验失败');
            }

            req._bridgeMachineId = machineId;
            req._bridgeMachine = machine;
            req._bridgeKeyId = keyId;
            req._bridgeTimestamp = timestamp;
            next();
        } catch (err) {
            next(err);
        }
    };
}

/**
 * Per-machineId 内存窗口速率限制器。简单足够用，进程级状态。
 * - 'trusted-controllers' / 'bridge-secret'：30s 间隔（最快每 30 秒一次）
 * - 'wrap-key'：60s 内最多 6 次
 */
function makeMachineRateLimiter(name, kind) {
    const minIntervalMs = kind === 'wrap-key' ? null : 30_000;
    const windowMs = kind === 'wrap-key' ? 60_000 : null;
    const maxPerWindow = kind === 'wrap-key' ? 6 : null;

    const lastHit = new Map(); // machineId -> timestampMs（间隔模式）
    const windowHits = new Map(); // machineId -> [timestampMs, ...]（窗口模式）

    return function rateLimit(req, res, next) {
        const machineId = String(req.params.machineId || '').trim();
        const now = Date.now();

        if (kind === 'wrap-key') {
            const arr = (windowHits.get(machineId) || []).filter((t) => now - t <= windowMs);
            if (arr.length >= maxPerWindow) {
                res.setHeader('Retry-After', Math.ceil((windowMs - (now - arr[0])) / 1000) || 1);
                return res.status(429).json({
                    success: false,
                    error: `${name} 触发速率限制（${maxPerWindow}/分钟）`,
                    retryAfterSeconds: Math.ceil((windowMs - (now - arr[0])) / 1000) || 1,
                });
            }
            arr.push(now);
            windowHits.set(machineId, arr);
            return next();
        }

        // 间隔模式
        const last = lastHit.get(machineId) || 0;
        if (last > 0 && now - last < minIntervalMs) {
            const wait = Math.ceil((minIntervalMs - (now - last)) / 1000) || 1;
            res.setHeader('Retry-After', wait);
            return res.status(429).json({
                success: false,
                error: `${name} 触发速率限制（${minIntervalMs / 1000}s 间隔）`,
                retryAfterSeconds: wait,
            });
        }
        lastHit.set(machineId, now);
        return next();
    };
}

/**
 * Revocation 通知 fire-and-forget POST（Wave 5 任务 10.2 / 决策点 4 落地）。
 *
 * 触发时机：trustedController 写操作（add / remove）、bridgeSecret 录入新版本，
 * 调用方传入 (machineId, reason)，本函数把通知 POST 给机台一侧的
 * RevocationListener（默认 http://127.0.0.1:7891/rustdesk/revoke）。
 *
 * 本 feature 假设 loopback 部署 / 内网路由 —— 反向通道**不**做签名验证，
 * 仅靠 loopback prefix + Windows 防火墙规则限制访问。
 *
 * machineId == '*' 表示广播：当前简化实现下（loopback / 单机部署）只 POST 一次；
 * 多机环境的真正广播由 SnapshotRefreshLoop / BridgeSecretClient 周期刷新自然兜底
 * （5–10 分钟内最终一致）。
 *
 * 错误一律吞噉（"5 分钟内未送达由 SnapshotRefreshLoop 自然失效兜底"）；
 * 超时 2 秒；用 node:http 避免引入额外依赖。
 *
 * @param {string} machineId 机台 ID 或 '*' 表示广播。
 * @param {string} reason 'denied' | 'secret_outdated'（与 RevocationFrame.reason 字面量集合一致）。
 * @param {{ port?: number, host?: string }} [options] 测试钩子，覆盖默认 host/port。
 */
function dispatchRevocationToMachine(machineId, reason, options = {}) {
    const port = Number.parseInt(
        options.port != null
            ? String(options.port)
            : (process.env.BRIDGE_REVOCATION_PORT || '7891'),
        10,
    );
    const host = options.host || process.env.BRIDGE_REVOCATION_HOST || '127.0.0.1';

    const body = JSON.stringify({
        reason,
        issuedAt: Date.now(),
        machineId: machineId == null ? null : String(machineId),
    });

    let req;
    try {
        req = http.request({
            method: 'POST',
            host,
            port,
            path: '/rustdesk/revoke',
            timeout: 2000,
            headers: {
                'content-type': 'application/json; charset=utf-8',
                'content-length': Buffer.byteLength(body, 'utf8'),
            },
        });
    } catch (err) {
        // eslint-disable-next-line no-console
        console.warn('[bridgeRoutes] dispatchRevocationToMachine 构造请求失败（已忽略）:', err.message);
        return;
    }

    // 错误一律吞 —— 反向通道是 best-effort
    req.on('error', (err) => {
        // eslint-disable-next-line no-console
        console.warn('[bridgeRoutes] dispatchRevocationToMachine 请求失败（已忽略）:', err.message);
    });
    req.on('timeout', () => {
        try { req.destroy(new Error('revocation POST timeout')); } catch { /* ignore */ }
    });
    // 响应不消费 —— 但要把 socket data 抽干，让 keep-alive 句柄能被回收
    req.on('response', (res) => {
        try { res.resume(); } catch { /* ignore */ }
    });
    try {
        req.write(body);
        req.end();
    } catch (err) {
        // eslint-disable-next-line no-console
        console.warn('[bridgeRoutes] dispatchRevocationToMachine 写入失败（已忽略）:', err.message);
    }
}

function createBridgeRoutes(deps) {
    const {
        trustedControllerStore,
        bridgeSecretStore,
        wrapKeyStore,
        policySigningStore,
        reportStore,
        writeAudit,
    } = deps || {};
    if (!trustedControllerStore) throw new Error('createBridgeRoutes: 缺少 trustedControllerStore');
    if (!bridgeSecretStore) throw new Error('createBridgeRoutes: 缺少 bridgeSecretStore');
    if (!wrapKeyStore) throw new Error('createBridgeRoutes: 缺少 wrapKeyStore');
    if (!policySigningStore) throw new Error('createBridgeRoutes: 缺少 policySigningStore');
    if (!reportStore) throw new Error('createBridgeRoutes: 缺少 reportStore');

    const { canonicalize: jcsCanonicalize } = require('./jcs');

    const router = express.Router();

    const auditOf = (req, fields) => {
        if (typeof writeAudit === 'function') writeAudit(req, fields);
    };

    // ---------- POST /api/machines/:machineId/rustdesk/report ----------
    router.post(
        '/:machineId/rustdesk/report',
        makeRequireBridgeMachineSignature('POST /api/machines/:machineId/rustdesk/report'),
        asyncHandler(async (req, res) => {
            const machineId = req._bridgeMachineId;
            const body = req.body || {};

            const rustDeskId = String(body.rustDeskId || '').trim();
            const passwordKind = String(body.passwordKind || '').trim();
            const wrapKeyId = String(body.wrapKeyId || '').trim();
            const ivB64 = String(body.iv || '').trim();
            const passwordCipherB64 = String(body.passwordCipher || '').trim();
            const authTagB64 = String(body.authTag || '').trim();
            const reportedAt = Number(body.reportedAt);
            const secretVersion = Number(body.secretVersion);

            if (!rustDeskId) throw createJsonError(400, 'rustDeskId 不能为空');
            if (!PASSWORD_KIND_ALLOWED.has(passwordKind)) throw createJsonError(400, 'passwordKind 非法');
            if (!Number.isFinite(reportedAt) || reportedAt < 0) throw createJsonError(400, 'reportedAt 非法');
            if (!Number.isFinite(secretVersion) || secretVersion < 0 || secretVersion > 4_294_967_295) {
                throw createJsonError(400, 'secretVersion 非法');
            }

            // passwordKind === 'absent' 时密文字段也接受为空字符串
            if (passwordKind !== 'absent') {
                if (!wrapKeyId || !ivB64 || !passwordCipherB64 || !authTagB64) {
                    throw createJsonError(400, '密码加密字段不完整');
                }
            }

            let passwordPlaintext = '';
            if (passwordKind !== 'absent') {
                const wrapKeyEntry = await wrapKeyStore.getKeyMaterial(wrapKeyId, machineId);
                if (!wrapKeyEntry) {
                    throw createJsonError(400, 'wrap_key 不存在', { errorCode: 'WRAP_KEY_EXPIRED' });
                }
                if (wrapKeyEntry.expired) {
                    throw createJsonError(400, 'wrap_key 已过期', { errorCode: 'WRAP_KEY_EXPIRED' });
                }
                let iv;
                let cipher;
                let authTag;
                try {
                    iv = Buffer.from(ivB64, 'base64');
                    cipher = Buffer.from(passwordCipherB64, 'base64');
                    authTag = Buffer.from(authTagB64, 'base64');
                } catch {
                    throw createJsonError(400, '密文字段 base64 解码失败');
                }
                if (iv.length !== 12) throw createJsonError(400, 'iv 长度必须为 12 字节');
                if (authTag.length !== 16) throw createJsonError(400, 'authTag 长度必须为 16 字节');

                // associated-data 严格按 §6.7.4 ASCII 字节串
                const aad = Buffer.from(
                    `VHDMounterRustDeskPasswordV1\n${machineId}\n${rustDeskId}\n${passwordKind}\n${reportedAt}`,
                    'utf8',
                );

                try {
                    const decipher = crypto.createDecipheriv('aes-256-gcm', wrapKeyEntry.keyMaterial, iv);
                    decipher.setAAD(aad);
                    decipher.setAuthTag(authTag);
                    const plain = Buffer.concat([decipher.update(cipher), decipher.final()]);
                    passwordPlaintext = plain.toString('utf8');
                } catch {
                    throw createJsonError(400, 'AAD 或 authTag 校验失败', {
                        errorCode: 'PAYLOAD_AAD_MISMATCH',
                    });
                }
            }

            const stored = await reportStore.upsertReport({
                machineId,
                rustDeskId,
                passwordKind,
                passwordPlaintext,
                lastWrapKeyId: passwordKind === 'absent' ? null : wrapKeyId,
                secretVersion,
                reportedAt,
            });

            // 审计行字段白名单（§15.9）：machineId / rustDeskId / passwordKind / hash 前缀 / secretVersion
            auditOf(req, {
                type: 'machine.rustdesk-report.upsert',
                actor: 'machine',
                result: 'success',
                machineId,
                rustDeskId,
                passwordKind,
                passwordHashPrefix: stored ? stored.passwordHashPrefix : null,
                secretVersion,
            });

            res.json({ success: true, stored: true });
        }),
    );

    // ---------- GET /api/machines/:machineId/rustdesk/trusted-controllers ----------
    router.get(
        '/:machineId/rustdesk/trusted-controllers',
        makeMachineRateLimiter('trusted-controllers', 'snapshot'),
        makeRequireBridgeMachineSignature('GET /api/machines/:machineId/rustdesk/trusted-controllers'),
        asyncHandler(async (req, res) => {
            const machineId = req._bridgeMachineId;
            await trustedControllerStore.ensureLoaded();
            const entries = await trustedControllerStore.listForMachine(machineId);
            const snapshotSeq = trustedControllerStore.currentSnapshotVersion();
            const issuedAt = Date.now();

            // 与 §15.10 / SnapshotStore.cs 同口径 —— 仅返回必要字段（不含 audit_note / created_at）
            const sanitizedEntries = entries.map((e) => {
                const obj = {
                    controllerId: e.controllerId,
                    scope: e.scope,
                    enabled: !!e.enabled,
                };
                if (e.controllerHwidHash) obj.controllerHwidHash = e.controllerHwidHash;
                if (e.expiresAt != null) obj.expiresAt = e.expiresAt; // ms
                return obj;
            });

            const entriesCanonical = jcsCanonicalize(sanitizedEntries);
            const entriesDigestHex = crypto.createHash('sha256')
                .update(entriesCanonical)
                .digest('hex');

            const payload =
                `TrustedControllersSnapshotV1\n${machineId}\n${snapshotSeq}\n${issuedAt}\n${entriesDigestHex}`;
            const sig = await policySigningStore.signPayload(Buffer.from(payload, 'ascii'));

            res.json({
                success: true,
                machineId,
                snapshotSeq,
                issuedAt,
                entries: sanitizedEntries,
                signature: sig.signatureBase64,
            });
        }),
    );

    // ---------- POST /api/machines/:machineId/rustdesk/wrap-key ----------
    router.post(
        '/:machineId/rustdesk/wrap-key',
        makeMachineRateLimiter('wrap-key', 'wrap-key'),
        makeRequireBridgeMachineSignature('POST /api/machines/:machineId/rustdesk/wrap-key'),
        asyncHandler(async (req, res) => {
            const machineId = req._bridgeMachineId;
            const machine = req._bridgeMachine;
            const issued = await wrapKeyStore.issue(machineId, machine.pubkey_pem, { ttlMs: TTL_WRAP_KEY_MS });

            const cipherDigestHex = crypto.createHash('sha256')
                .update(issued.wrapKeyCipher.toString('base64'), 'utf8')
                .digest('hex');
            const payload =
                `VHDMounterWrapKeyResponseV1\n${machineId}\n${issued.wrapKeyId}\n${cipherDigestHex}\n${issued.issuedAtMs}\n${issued.ttlMs}`;
            const sig = await policySigningStore.signPayload(Buffer.from(payload, 'ascii'));

            auditOf(req, {
                type: 'machine.rustdesk-wrap-key.issue',
                actor: 'machine',
                result: 'success',
                machineId,
                wrapKeyId: issued.wrapKeyId,
                ttlMs: issued.ttlMs,
            });

            res.json({
                success: true,
                wrapKeyId: issued.wrapKeyId,
                wrapKeyCipher: issued.wrapKeyCipher.toString('base64'),
                issuedAt: issued.issuedAtMs,
                ttlMs: issued.ttlMs,
                signature: sig.signatureBase64,
            });
        }),
    );

    // ---------- GET /api/machines/:machineId/rustdesk/policy-pubkey ----------
    router.get(
        '/:machineId/rustdesk/policy-pubkey',
        makeRequireBridgeMachineSignature('GET /api/machines/:machineId/rustdesk/policy-pubkey'),
        asyncHandler(async (req, res) => {
            const machineId = req._bridgeMachineId;
            const active = await policySigningStore.getActiveSigningKey();
            const issuedAt = Date.now();

            // BridgePolicyPubkeyV1 payload = "BridgePolicyPubkeyV1\n<machineId>\n<sha256Hex(pubkey PEM)>\n<issuedAt>"
            // 用机台**注册证书私钥** (securityStore.signingPrivateKey) 签名 + 一并下发 registrationCertPem
            // 让机台用 RegistrationCertificatePath 锚点验签。
            const pubkeyDigestHex = crypto.createHash('sha256')
                .update(active.publicKeyPem, 'utf8')
                .digest('hex');
            const payload =
                `BridgePolicyPubkeyV1\n${machineId}\n${pubkeyDigestHex}\n${issuedAt}`;

            const runtime = req.app.locals.runtime;
            const securityConfig = runtime.securityConfig || {};
            const signingKeyPem = securityConfig.signingPrivateKey
                || securityConfig.signing_private_key
                || securityConfig.serverSigningPrivateKey;
            const registrationCertPem = securityConfig.signingCertificatePem
                || securityConfig.signing_certificate_pem
                || securityConfig.serverSigningCertificatePem
                || '';

            if (!signingKeyPem) {
                throw createJsonError(503, '服务端尚未配置注册证书签名私钥');
            }

            const policySignature = crypto.sign('sha256', Buffer.from(payload, 'ascii'), {
                key: signingKeyPem,
                padding: crypto.constants.RSA_PKCS1_PADDING,
            }).toString('base64');

            res.json({
                success: true,
                publicKeyPem: active.publicKeyPem,
                issuedAt,
                policySignature,
                registrationCertPem,
            });
        }),
    );

    // ---------- GET /api/machines/:machineId/rustdesk/bridge-secret ----------
    router.get(
        '/:machineId/rustdesk/bridge-secret',
        makeMachineRateLimiter('bridge-secret', 'snapshot'),
        makeRequireBridgeMachineSignature('GET /api/machines/:machineId/rustdesk/bridge-secret'),
        asyncHandler(async (req, res) => {
            const machineId = req._bridgeMachineId;
            const machine = req._bridgeMachine;

            const active = await bridgeSecretStore.getActive();
            if (!active) {
                throw createJsonError(404, '尚未录入任何 RustDeskClientSharedSecret 版本', {
                    errorCode: 'NO_ACTIVE_BRIDGE_SECRET',
                });
            }

            // 用机台 TPM 公钥 RSA-OAEP-SHA256 包裹 32 字节 secret
            let cipherBuffer;
            try {
                cipherBuffer = crypto.publicEncrypt({
                    key: machine.pubkey_pem,
                    padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
                    oaepHash: 'sha256',
                }, active.keyMaterial);
            } catch (err) {
                throw createJsonError(500, '机台公钥包裹 secret 失败：' + err.message);
            }

            const cipherBase64 = cipherBuffer.toString('base64');
            const issuedAt = Date.now();

            // BridgeSecretResponseV1 payload = "BridgeSecretResponseV1\n<machineId>\n<secretVersion>\n<sha256Hex(secretCipher base64)>\n<issuedAt>"
            const cipherDigestHex = crypto.createHash('sha256')
                .update(cipherBase64, 'utf8')
                .digest('hex');
            const payload =
                `BridgeSecretResponseV1\n${machineId}\n${active.secretVersion}\n${cipherDigestHex}\n${issuedAt}`;
            const sig = await policySigningStore.signPayload(Buffer.from(payload, 'ascii'));

            res.json({
                success: true,
                secretVersion: active.secretVersion,
                secretCipher: cipherBase64,
                issuedAt,
                signature: sig.signatureBase64,
            });
        }),
    );

    return {
        router,
        requireBridgeMachineSignature: makeRequireBridgeMachineSignature,
        dispatchRevocationToMachine,
    };
}

module.exports = {
    createBridgeRoutes,
    dispatchRevocationToMachine,
    ALLOWED_PAYLOAD_VERSIONS,
};
