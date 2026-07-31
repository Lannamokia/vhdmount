/**
 * 保持性属性测试 — 现有部署流程行为
 *
 * **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11, 3.12, 3.13, 3.14, 3.15, 3.16**
 *
 * 这些测试保护现有正确行为不被修复引入的回归破坏。
 * 在未修复代码上运行时，测试应 PASS —— 通过即确认基线行为正常。
 *
 * 覆盖观察：
 * - 合法 packageId（^pkg-[a-f0-9]{32}$）通过校验且下载成功
 * - 有效 token（未使用、未过期）成功认证
 * - 合法状态转换 pending→downloading→running→success 正常完成
 * - 有效 Range 请求返回正确字节范围
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
            if (sql.includes('INSERT INTO deployment_tasks')) {
                const task = { task_id: params[0], package_id: params[1], machine_id: params[2], task_type: params[3] || 'deploy', status: 'pending', scheduled_at: params[4], started_at: null, completed_at: null, lease_expires_at: null, error_message: null, created_at: new Date().toISOString() };
                tasks.set(params[0], task);
                return { rows: [task] };
            }
            if (sql.includes('SELECT * FROM deployment_tasks WHERE task_id')) {
                return { rows: [tasks.get(params[0])].filter(Boolean) };
            }
            if (sql.includes("t.status IN ('downloading', 'running')")) {
                const claimable = Array.from(tasks.values())
                    .filter((task) => {
                        if (task.machine_id !== params[0]) return false;
                        if (task.scheduled_at && Date.parse(task.scheduled_at) > Date.now()) return false;
                        if (task.status === 'pending') return true;
                        if ((task.status === 'downloading' || task.status === 'running') && task.lease_expires_at) {
                            return Date.parse(task.lease_expires_at) <= Date.now();
                        }
                        return false;
                    })
                    .map((task) => {
                        const pkg = packages.get(task.package_id);
                        return { ...task, name: pkg?.name ?? 'Test', version: pkg?.version ?? '1.0', type: pkg?.type ?? 'software-deploy', file_size: pkg?.file_size ?? 1024 };
                    });
                return { rows: claimable };
            }
            if (sql.includes('UPDATE deployment_tasks')) {
                const task = tasks.get(params[0]);
                if (task) {
                    if (sql.includes("SET status = 'downloading'")) {
                        task.status = 'downloading';
                        task.started_at ??= new Date().toISOString();
                        task.completed_at = null;
                        task.error_message = null;
                        task.lease_expires_at = new Date(Date.now() + (Number(params[1]) * 1000)).toISOString();
                    } else {
                        task.status = params[1] || task.status;
                        if (sql.includes('started_at = COALESCE(started_at, NOW())')) task.started_at ??= new Date().toISOString();
                        if (sql.includes('completed_at = NOW()')) task.completed_at = new Date().toISOString();
                        if (sql.includes('lease_expires_at = NOW() +')) {
                            const leaseSeconds = Number(params[2]);
                            if (Number.isFinite(leaseSeconds)) task.lease_expires_at = new Date(Date.now() + (leaseSeconds * 1000)).toISOString();
                        }
                        if (sql.includes('lease_expires_at = NULL')) task.lease_expires_at = null;
                        if (sql.includes('error_message = NULL')) task.error_message = null;
                        if (params.length > 2) {
                            const lastParam = params[params.length - 1];
                            if (typeof lastParam === 'string' && !Number.isFinite(Number(lastParam)) && lastParam !== task.status) task.error_message = lastParam;
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
            if (sql.includes('UPDATE deployment_tokens') && sql.includes('SET used_at')) {
                const token = tokens.get(params[0]);
                if (token && token.machine_id === params[1] && token.package_id === params[2] && token.resource_type === params[3]) {
                    token.used_at = token.used_at || new Date().toISOString();
                    return { rows: [token], rowCount: 1 };
                }
                if (token) {
                    token.used_at = token.used_at || new Date().toISOString();
                    return { rows: [token], rowCount: 1 };
                }
                return { rows: [], rowCount: 0 };
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
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-preservation-'));
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
    const keys = runtime.securityConfig.totpKeys || [];
    const secret = keys.length > 0 ? keys[0].secret : runtime.securityConfig.totpSecret;
    await client.post('/api/auth/otp/verify').send({ code: authenticator.generate(secret) }).expect(200);
}

// ========== 属性测试 1：合法 packageId 通过校验且下载成功 ==========

test('保持性 - 合法 packageId (^pkg-[a-f0-9]{32}$) 通过校验且下载成功', async (t) => {
    // **Validates: Requirements 3.15**
    // 观察：匹配正则的合法 packageId 在未修复代码上通过校验且下载成功
    const { app, database, tempDir } = await createInitializedHarness(t);

    // 生成多个随机合法 packageId 进行属性测试
    for (let i = 0; i < 10; i++) {
        const packageId = `pkg-${crypto.randomBytes(16).toString('hex')}`;
        assert.ok(/^pkg-[a-f0-9]{32}$/.test(packageId), `生成的 packageId 应匹配正则: ${packageId}`);

        // 创建包和文件
        const filePath = path.join(tempDir, 'deployment-packages', `${packageId}.zip`);
        const sigPath = path.join(tempDir, 'deployment-packages', `${packageId}.zip.sig`);
        fs.mkdirSync(path.dirname(filePath), { recursive: true });
        const fileContent = crypto.randomBytes(64);
        fs.writeFileSync(filePath, fileContent);
        fs.writeFileSync(sigPath, crypto.randomBytes(32));

        await database.query(
            'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
            [packageId, `TestPkg-${i}`, '1.0.0', 'software-deploy', 'test', filePath, fileContent.length, new Date(Date.now() + 86400000).toISOString()]
        );

        // 创建有效 token（参数顺序匹配 fake DB 期望：token, taskId, machineId, packageId, resourceType, aesKey, aesIv）
        const tokenValue = crypto.randomBytes(32).toString('hex');
        const aesKey = crypto.randomBytes(32).toString('base64');
        const aesIv = Buffer.concat([crypto.randomBytes(8), Buffer.alloc(8)]).toString('base64');
        await database.query(
            'INSERT INTO deployment_tokens (token, task_id, machine_id, package_id, resource_type, aes_key, aes_iv) VALUES ($1, $2, $3, $4, $5, $6, $7)',
            [tokenValue, `task-${i}`, 'machine-001', packageId, 'package', aesKey, aesIv]
        );

        // 下载应成功
        const res = await request(app)
            .get(`/api/deployments/packages/${packageId}/download?token=${tokenValue}&machineId=machine-001`)
            .set('User-Agent', 'VHDMount/1.0.0');

        assert.strictEqual(res.status, 200,
            `合法 packageId "${packageId}" 下载应返回 200，实际返回 ${res.status}`);
    }
});

// ========== 属性测试 2：有效未使用 token 认证成功一次 ==========

test('保持性 - 有效未使用 token 成功认证', async (t) => {
    // **Validates: Requirements 3.8**
    // 观察：有效 token（未使用、未过期）在未修复代码上成功认证
    const { app, database, tempDir } = await createInitializedHarness(t);

    // 生成多个随机 token 进行属性测试
    for (let i = 0; i < 5; i++) {
        const packageId = `pkg-${crypto.randomBytes(16).toString('hex')}`;
        const filePath = path.join(tempDir, 'deployment-packages', `${packageId}.zip`);
        fs.mkdirSync(path.dirname(filePath), { recursive: true });
        const fileContent = crypto.randomBytes(32 + Math.floor(Math.random() * 128));
        fs.writeFileSync(filePath, fileContent);

        await database.query(
            'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
            [packageId, `TokenTest-${i}`, '1.0.0', 'software-deploy', 'test', filePath, fileContent.length, new Date(Date.now() + 86400000).toISOString()]
        );

        const tokenValue = crypto.randomBytes(32).toString('hex');
        const aesKey = crypto.randomBytes(32).toString('base64');
        const aesIv = Buffer.concat([crypto.randomBytes(8), Buffer.alloc(8)]).toString('base64');
        await database.query(
            'INSERT INTO deployment_tokens (token, task_id, machine_id, package_id, resource_type, aes_key, aes_iv) VALUES ($1, $2, $3, $4, $5, $6, $7)',
            [tokenValue, `task-token-${i}`, 'machine-001', packageId, 'package', aesKey, aesIv]
        );

        // 第一次使用应成功
        const res = await request(app)
            .get(`/api/deployments/packages/${packageId}/download?token=${tokenValue}&machineId=machine-001`)
            .set('User-Agent', 'VHDMount/1.0.0');

        assert.strictEqual(res.status, 200,
            `有效未使用 token 第一次使用应返回 200，实际返回 ${res.status}`);
    }
});

// ========== 属性测试 3：合法状态转换正常完成 ==========

test('保持性 - 合法状态转换 pending→downloading→running→success 正常完成', async (t) => {
    // **Validates: Requirements 3.9, 3.10**
    // 观察：合法状态转换在未修复代码上正常完成
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    // 定义合法状态转换序列
    const validTransitions = [
        { from: 'pending', to: 'downloading' },
        { from: 'downloading', to: 'running' },
        { from: 'running', to: 'success' },
    ];

    // 创建任务并逐步转换
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-transition-001', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );

    for (const { from, to } of validTransitions) {
        const statusPath = '/api/machines/machine-001/deployments/task-transition-001/status';
        const body = JSON.stringify({ status: to });

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

        assert.strictEqual(res.status, 200,
            `合法转换 ${from}→${to} 应返回 200，实际返回 ${res.status}`);
        assert.strictEqual(res.body.task.status, to,
            `转换后状态应为 ${to}，实际为 ${res.body.task.status}`);
    }
});

test('保持性 - 合法状态转换 downloading→failed 正常完成', async (t) => {
    // **Validates: Requirements 3.10**
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-fail-dl-001', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );

    // pending → downloading
    let statusPath = '/api/machines/machine-001/deployments/task-fail-dl-001/status';
    let body = JSON.stringify({ status: 'downloading' });
    let res = await request(app)
        .post(statusPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({ privateKey: testKeyPair.privateKey, machineId: 'machine-001', keyId: testKeyId, method: 'POST', path: statusPath, body }))
        .send(body);
    assert.strictEqual(res.status, 200);

    // downloading → failed
    body = JSON.stringify({ status: 'failed', errorMessage: '下载超时' });
    res = await request(app)
        .post(statusPath)
        .set('Content-Type', 'application/json')
        .set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({ privateKey: testKeyPair.privateKey, machineId: 'machine-001', keyId: testKeyId, method: 'POST', path: statusPath, body }))
        .send(body);

    assert.strictEqual(res.status, 200, `downloading→failed 应返回 200，实际返回 ${res.status}`);
    assert.strictEqual(res.body.task.status, 'failed');
});

test('保持性 - 合法状态转换 running→failed 正常完成', async (t) => {
    // **Validates: Requirements 3.10**
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);

    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-fail-run-001', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );

    const statusPath = '/api/machines/machine-001/deployments/task-fail-run-001/status';

    // pending → downloading
    let body = JSON.stringify({ status: 'downloading' });
    await request(app).post(statusPath).set('Content-Type', 'application/json').set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({ privateKey: testKeyPair.privateKey, machineId: 'machine-001', keyId: testKeyId, method: 'POST', path: statusPath, body }))
        .send(body).expect(200);

    // downloading → running
    body = JSON.stringify({ status: 'running' });
    await request(app).post(statusPath).set('Content-Type', 'application/json').set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({ privateKey: testKeyPair.privateKey, machineId: 'machine-001', keyId: testKeyId, method: 'POST', path: statusPath, body }))
        .send(body).expect(200);

    // running → failed
    body = JSON.stringify({ status: 'failed', errorMessage: '安装脚本执行失败' });
    const res = await request(app).post(statusPath).set('Content-Type', 'application/json').set('User-Agent', 'VHDMount/1.0.0')
        .set(createMachineAuthHeaders({ privateKey: testKeyPair.privateKey, machineId: 'machine-001', keyId: testKeyId, method: 'POST', path: statusPath, body }))
        .send(body);

    assert.strictEqual(res.status, 200, `running→failed 应返回 200，实际返回 ${res.status}`);
    assert.strictEqual(res.body.task.status, 'failed');
});

// ========== 属性测试 4：有效 Range 请求返回正确字节范围 ==========

test('保持性 - 有效 Range 请求 (start >= 0 && start < fileSize) 返回正确字节范围', async (t) => {
    // **Validates: Requirements 3.3**
    // 观察：有效 Range 请求在未修复代码上返回正确字节范围
    const { app, database, tempDir } = await createInitializedHarness(t);

    const packageId = `pkg-${crypto.randomBytes(16).toString('hex')}`;
    const filePath = path.join(tempDir, 'deployment-packages', `${packageId}.zip`);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    const fileContent = crypto.randomBytes(256); // 256 bytes
    fs.writeFileSync(filePath, fileContent);

    await database.query(
        'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        [packageId, 'RangeTest', '1.0.0', 'software-deploy', 'test', filePath, fileContent.length, new Date(Date.now() + 86400000).toISOString()]
    );

    // 测试多个有效 Range 请求
    const validRanges = [
        { start: 0, end: 63 },      // 前 64 字节
        { start: 64, end: 127 },     // 中间 64 字节
        { start: 128, end: 255 },    // 后 128 字节
        { start: 0, end: 255 },      // 全部
        { start: 100, end: 200 },    // 任意有效范围
    ];

    for (const { start, end } of validRanges) {
        const tokenValue = crypto.randomBytes(32).toString('hex');
        const aesKey = crypto.randomBytes(32).toString('base64');
        const aesIv = Buffer.concat([crypto.randomBytes(8), Buffer.alloc(8)]).toString('base64');
        await database.query(
            'INSERT INTO deployment_tokens (token, task_id, machine_id, package_id, resource_type, aes_key, aes_iv) VALUES ($1, $2, $3, $4, $5, $6, $7)',
            [tokenValue, `task-range-${start}`, 'machine-001', packageId, 'package', aesKey, aesIv]
        );

        const res = await request(app)
            .get(`/api/deployments/packages/${packageId}/download?token=${tokenValue}&machineId=machine-001`)
            .set('User-Agent', 'VHDMount/1.0.0')
            .set('Range', `bytes=${start}-${end}`);

        assert.strictEqual(res.status, 206,
            `有效 Range bytes=${start}-${end} 应返回 206，实际返回 ${res.status}`);

        const expectedLength = end - start + 1;
        assert.strictEqual(Number(res.headers['content-length']), expectedLength,
            `Content-Length 应为 ${expectedLength}，实际为 ${res.headers['content-length']}`);

        const contentRange = res.headers['content-range'];
        assert.ok(contentRange, 'Range 响应应包含 Content-Range 头');
        assert.ok(contentRange.includes(`bytes ${start}-${end}/${fileContent.length}`),
            `Content-Range 应为 bytes ${start}-${end}/${fileContent.length}，实际为 ${contentRange}`);
    }
});
