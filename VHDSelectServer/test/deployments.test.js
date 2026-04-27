const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const request = require('supertest');
const { createApp } = require('../server');

function createFakeDatabase() {
    const machines = new Map();
    const packages = new Map();
    const tasks = new Map();
    const tokens = new Map();
    const records = new Map();

    return {
        async initialize() { },
        async close() { },

        async getMachine(machineId) {
            return machines.get(machineId) || null;
        },
        async upsertMachine(machineId, isProtected, vhdKeyword) {
            const machine = {
                machine_id: machineId,
                protected: isProtected ?? false,
                vhd_keyword: vhdKeyword ?? 'SDEZ',
                evhd_password_configured: false,
                pubkey_pem: null,
            };
            machines.set(machineId, machine);
            return machine;
        },
        async updateMachineKey(machineId, { keyId, keyType, pubkeyPem }) {
            const machine = machines.get(machineId);
            if (machine) {
                machine.key_id = keyId;
                machine.key_type = keyType;
                machine.pubkey_pem = pubkeyPem;
            }
            return machine || null;
        },
        async updateMachineLastSeen(machineId) {
            return new Date().toISOString();
        },

        // Deployment-related queries (used by deploymentStore)
        async query(sql, params) {
            // Simplified: parse SQL for test purposes
            if (sql.includes('INSERT INTO deployment_packages')) {
                const pkg = {
                    package_id: params[0],
                    name: params[1],
                    version: params[2],
                    type: params[3],
                    signer: params[4],
                    file_path: params[5],
                    file_size: params[6],
                    created_at: new Date().toISOString(),
                    expires_at: params[7],
                };
                packages.set(params[0], pkg);
                return { rows: [pkg] };
            }
            if (sql.includes('SELECT * FROM deployment_packages WHERE package_id')) {
                return { rows: [packages.get(params[0])].filter(Boolean) };
            }
            if (sql.includes('SELECT * FROM deployment_packages ORDER BY created_at')) {
                return { rows: Array.from(packages.values()).sort((a, b) => new Date(b.created_at) - new Date(a.created_at)) };
            }
            if (sql.includes('DELETE FROM deployment_packages')) {
                const pkg = packages.get(params[0]);
                packages.delete(params[0]);
                return { rows: pkg ? [pkg] : [] };
            }
            if (sql.includes('INSERT INTO deployment_tasks')) {
                const task = {
                    task_id: params[0],
                    package_id: params[1],
                    machine_id: params[2],
                    task_type: params[3] || 'deploy',
                    status: 'pending',
                    scheduled_at: params[4],
                    created_at: new Date().toISOString(),
                };
                tasks.set(params[0], task);
                return { rows: [task] };
            }
            if (sql.includes('SELECT * FROM deployment_tasks WHERE task_id')) {
                return { rows: [tasks.get(params[0])].filter(Boolean) };
            }
            if (sql.includes('ORDER BY created_at DESC')) {
                return { rows: Array.from(tasks.values()).sort((a, b) => new Date(b.created_at) - new Date(a.created_at)) };
            }
            if (sql.includes('machine_id = $1 AND t.status = \'pending\'')) {
                const pending = Array.from(tasks.values())
                    .filter(t => t.machine_id === params[0] && t.status === 'pending')
                    .map(t => ({ ...t, name: 'Test', version: '1.0', type: 'software-deploy', file_size: 1024 }));
                return { rows: pending };
            }
            if (sql.includes('UPDATE deployment_tasks')) {
                const task = tasks.get(params[0]);
                if (task) {
                    if (params[1]) task.status = params[1];
                    if (params.length > 2 && params[2]) task.error_message = params[2];
                }
                return { rows: [task].filter(Boolean) };
            }
            if (sql.includes('INSERT INTO deployment_tokens')) {
                // SQL: VALUES ($1,$2,$3,$4,$5,NOW()+INTERVAL'60 minutes',$6,$7)
                // expires_at is hardcoded in SQL, not a param
                const token = {
                    token: params[0],
                    task_id: params[1],
                    machine_id: params[2],
                    package_id: params[3],
                    resource_type: params[4],
                    expires_at: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
                    aes_key: params[5] || null,
                    aes_iv: params[6] || null,
                };
                tokens.set(params[0], token);
                return { rows: [token] };
            }
            if (sql.includes('deployment_tokens') && sql.includes('token = $1')) {
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
                const record = {
                    record_id: params[0],
                    machine_id: params[1],
                    package_id: params[2],
                    name: params[3],
                    version: params[4],
                    type: params[5],
                    target_path: params[6],
                    status: params[7],
                    deployed_at: params[8],
                    uninstalled_at: params[9],
                };
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

        async withClient(work) {
            return work(this);
        },
        async withTransaction(work) {
            return work(this);
        },

        async getMachineLogRuntimeSettings() {
            return { defaultRetentionActiveDays: 7, dailyInspectionHour: 3, dailyInspectionMinute: 0, timezone: 'UTC', lastInspectionAt: null };
        },
        async updateMachineLogRuntimeSettings() { },
    };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-deploy-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();

    const { app, runtime } = await createApp({
        logger: { log: () => {}, error: () => {}, info: () => {} },
        database: fakeDatabase,
        configDir: tempDir,
        disableSignalHandlers: true,
    });

    // Initialize security store with a password for auth
    const securityStore = runtime.securityStore;
    securityStore.saveSecurityConfig({
        passwordHash: '$2a$10$testhashtesthashtesthashtesthashtesthash',
        sessionSecret: 'test-session-secret-test-session-secret',
        totpSecret: 'JBSWY3DPEHPK3PXP',
        totpIssuer: 'Test',
        totpAccountName: 'admin',
        dbConfig: { host: 'localhost', database: 'test', user: 'test', password: 'test' },
        allowedOrigins: ['http://localhost:3000'],
        trustedRegistrationCertificates: [],
    });
    fs.writeFileSync(securityStore.getPaths().lockFile, '');
    runtime.initialized = true;
    runtime.securityConfig = securityStore.loadSecurityConfig();
    runtime.database = fakeDatabase;

    // 注册一个带公钥的测试机台，供部署加密流程使用
    const { generateKeyPairSync } = require('crypto');
    const testKeyPair = generateKeyPairSync('rsa', { modulusLength: 2048 });
    const testPubKeyPem = testKeyPair.publicKey.export({ type: 'spki', format: 'pem' });
    await fakeDatabase.upsertMachine('machine-001', false, 'SDEZ');
    await fakeDatabase.updateMachineKey('machine-001', {
        keyId: 'test-key-001',
        keyType: 'RSA',
        pubkeyPem: testPubKeyPem,
    });

    return { app, runtime, tempDir, database: fakeDatabase, testKeyPair };
}

test('GET /api/deployments/packages 需要认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).get('/api/deployments/packages');
    assert.strictEqual(res.status, 401);
    assert.strictEqual(res.body.requireAuth, true);
});

test('POST /api/deployments/tasks 需要认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).post('/api/deployments/tasks').send({
        packageId: 'pkg-test',
        targetMachineIds: ['machine-001'],
    });
    assert.strictEqual(res.status, 401);
});

test('POST /api/machines/:machineId/deployments/:recordId/uninstall 需要认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).post('/api/machines/machine-001/deployments/rec-test/uninstall');
    assert.strictEqual(res.status, 401);
});

test('GET /api/machines/:machineId/deployments/pending 不需要认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).get('/api/machines/machine-001/deployments/pending');
    // 返回空任务列表（没有 pending 任务）
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.success, true);
    assert.deepStrictEqual(res.body.tasks, []);
});

test('POST /api/machines/:machineId/deployments/:taskId/status 不需要认证', async (t) => {
    const { app, database } = await createInitializedHarness(t);

    // 先创建一个任务
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-001', 'pkg-001', 'machine-001', 'deploy', 'pending', null]
    );

    const res = await request(app).post('/api/machines/machine-001/deployments/task-001/status').send({
        status: 'success',
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.success, true);
    assert.strictEqual(res.body.task.status, 'success');
});

test('POST /api/machines/:machineId/deployments/sync 不需要认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).post('/api/machines/machine-001/deployments/sync').send({
        records: [{
            recordId: 'rec-001',
            packageId: 'pkg-001',
            name: 'Test',
            version: '1.0.0',
            type: 'software-deploy',
            status: 'success',
            deployedAt: new Date().toISOString(),
        }],
    });
    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.success, true);
    assert.strictEqual(res.body.synced, 1);
});

test('下载接口拒绝无 UA 的请求', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).get('/api/deployments/packages/pkg-001/download?token=abc&machineId=m1');
    assert.strictEqual(res.status, 403);
});

test('下载接口拒绝错误 UA 的请求', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app)
        .get('/api/deployments/packages/pkg-001/download?token=abc&machineId=m1')
        .set('User-Agent', 'Mozilla/5.0');
    assert.strictEqual(res.status, 403);
});

// ---------- 端到端加密传输专项测试 ----------

test('GET /api/machines/:machineId/deployments/pending 无机台公钥时返回 400', async (t) => {
    const { app, database } = await createInitializedHarness(t);

    // 创建无机台公钥的机台
    await database.upsertMachine('machine-nokey', false, 'SDEZ');

    const res = await request(app)
        .get('/api/machines/machine-nokey/deployments/pending')
        .set('User-Agent', 'VHDMount:1.0.0');

    assert.strictEqual(res.status, 400);
    assert.strictEqual(res.body.success, false);
});

test('GET /api/machines/:machineId/deployments/pending 有任务时返回加密字段', async (t) => {
    const { app, database, tempDir } = await createInitializedHarness(t);

    // 创建 ZIP 文件
    const zipPath = path.join(tempDir, 'test-pkg.zip');
    fs.writeFileSync(zipPath, 'fake zip content for testing');

    // 直接插入 package 和 task
    await database.query(
        'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        ['pkg-enc-001', 'TestPackage', '1.0.0', 'software-deploy', 'test', zipPath, 28, new Date(Date.now() + 30 * 86400000).toISOString()]
    );
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-enc-001', 'pkg-enc-001', 'machine-001', 'deploy', 'pending', null]
    );

    const res = await request(app)
        .get('/api/machines/machine-001/deployments/pending')
        .set('User-Agent', 'VHDMount:1.0.0');

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.success, true);
    assert.strictEqual(res.body.tasks.length, 1);

    const task = res.body.tasks[0];
    assert.strictEqual(task.taskId, 'task-enc-001');
    assert.strictEqual(task.packageId, 'pkg-enc-001');
    assert.ok(task.keyCipher, '应返回 keyCipher');
    assert.ok(task.iv, '应返回 iv');
    assert.ok(task.downloadUrl, '应返回 downloadUrl');
    assert.ok(task.signatureUrl, '应返回 signatureUrl');
});

test('downloadPackage 有效 token 返回 AES-CTR 加密流，可被正确解密', async (t) => {
    const { app, database, tempDir, testKeyPair } = await createInitializedHarness(t);

    // 创建 ZIP 文件（用随机数据确保不是纯文本巧合匹配）
    const zipContent = crypto.randomBytes(256);
    const zipPath = path.join(tempDir, 'test-enc.zip');
    fs.writeFileSync(zipPath, zipContent);

    await database.query(
        'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        ['pkg-dl-001', 'DlPkg', '1.0.0', 'software-deploy', 'test', zipPath, zipContent.length, new Date(Date.now() + 30 * 86400000).toISOString()]
    );
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-dl-001', 'pkg-dl-001', 'machine-001', 'deploy', 'pending', null]
    );

    // 获取 pending tasks（触发加密密钥生成）
    const pendingRes = await request(app)
        .get('/api/machines/machine-001/deployments/pending')
        .set('User-Agent', 'VHDMount:1.0.0');

    assert.strictEqual(pendingRes.status, 200);
    const task = pendingRes.body.tasks[0];

    // 用 RSA 私钥解密 keyCipher 得到 AES 密钥
    const keyCipherBytes = Buffer.from(task.keyCipher, 'base64');
    const aesKeyBase64 = crypto.privateDecrypt({
        key: testKeyPair.privateKey,
        padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha1',
    }, keyCipherBytes);
    const aesKey = Buffer.from(aesKeyBase64.toString('utf8'), 'base64');
    const iv = Buffer.from(task.iv, 'base64');

    // 解析下载 URL
    const downloadUrl = new URL(task.downloadUrl, 'http://localhost');
    const downloadToken = downloadUrl.searchParams.get('token');

    // 下载加密流
    const dlRes = await request(app)
        .get(`/api/deployments/packages/pkg-dl-001/download?token=${downloadToken}&machineId=machine-001`)
        .set('User-Agent', 'VHDMount:1.0.0')
        .buffer(true)
        .parse((res, callback) => {
            res.data = '';
            res.setEncoding('binary');
            res.on('data', (chunk) => { res.data += chunk; });
            res.on('end', () => callback(null, Buffer.from(res.data, 'binary')));
        });

    assert.strictEqual(dlRes.status, 200);
    assert.strictEqual(dlRes.body.length, zipContent.length);

    // CTR 解密（Node.js 原生支持）
    const decipher = crypto.createDecipheriv('aes-256-ctr', aesKey, iv);
    const decrypted = Buffer.concat([decipher.update(dlRes.body), decipher.final()]);

    assert.deepStrictEqual(decrypted, zipContent);
});

test('downloadPackage Range 请求返回正确偏移的加密流', async (t) => {
    const { app, database, tempDir, testKeyPair } = await createInitializedHarness(t);

    // 创建稍大的 ZIP 文件（跨越多个 block）
    const zipContent = crypto.randomBytes(200); // 200 bytes > 12 blocks
    const zipPath = path.join(tempDir, 'test-range.zip');
    fs.writeFileSync(zipPath, zipContent);

    await database.query(
        'INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)',
        ['pkg-range-001', 'RangePkg', '1.0.0', 'software-deploy', 'test', zipPath, zipContent.length, new Date(Date.now() + 30 * 86400000).toISOString()]
    );
    await database.query(
        'INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at) VALUES ($1, $2, $3, $4, $5, $6)',
        ['task-range-001', 'pkg-range-001', 'machine-001', 'deploy', 'pending', null]
    );

    // 获取 pending tasks
    const pendingRes = await request(app)
        .get('/api/machines/machine-001/deployments/pending')
        .set('User-Agent', 'VHDMount:1.0.0');

    const task = pendingRes.body.tasks[0];

    // 解密 AES 密钥
    const keyCipherBytes = Buffer.from(task.keyCipher, 'base64');
    const aesKeyBase64 = crypto.privateDecrypt({
        key: testKeyPair.privateKey,
        padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha1',
    }, keyCipherBytes);
    const aesKey = Buffer.from(aesKeyBase64.toString('utf8'), 'base64');
    const iv = Buffer.from(task.iv, 'base64');

    // 解析 token
    const downloadUrl = new URL(task.downloadUrl, 'http://localhost');
    const downloadToken = downloadUrl.searchParams.get('token');

    // Range 请求：从 byte 50 开始
    const rangeStart = 50;
    const dlRes = await request(app)
        .get(`/api/deployments/packages/pkg-range-001/download?token=${downloadToken}&machineId=machine-001`)
        .set('User-Agent', 'VHDMount:1.0.0')
        .set('Range', `bytes=${rangeStart}-`)
        .buffer(true)
        .parse((res, callback) => {
            res.data = '';
            res.setEncoding('binary');
            res.on('data', (chunk) => { res.data += chunk; });
            res.on('end', () => callback(null, Buffer.from(res.data, 'binary')));
        });

    assert.strictEqual(dlRes.status, 206);
    const expectedLength = zipContent.length - rangeStart;
    assert.strictEqual(dlRes.body.length, expectedLength);

    // 服务端 createCtrCipher 逻辑：根据 offset 调整 counter
    const blockSize = 16;
    const counter = Math.floor(rangeStart / blockSize);
    const blockOffset = rangeStart % blockSize;

    const ivBuf = Buffer.alloc(16);
    iv.copy(ivBuf, 0, 0, 8);
    const counterBuf = Buffer.alloc(8);
    counterBuf.writeBigUInt64BE(BigInt(counter), 0);
    counterBuf.copy(ivBuf, 8);

    const decipher = crypto.createDecipheriv('aes-256-ctr', aesKey, ivBuf);
    if (blockOffset > 0) {
        decipher.update(Buffer.alloc(blockOffset)); // 消耗掉 blockOffset 字节的 keystream
    }

    const decrypted = Buffer.concat([decipher.update(dlRes.body), decipher.final()]);
    const expectedSlice = zipContent.slice(rangeStart);

    assert.deepStrictEqual(decrypted, expectedSlice);
});
