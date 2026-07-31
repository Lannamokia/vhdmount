const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
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
                approved: false,
                evhd_password_configured: false,
                pubkey_pem: null,
                revoked: false,
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

        async query(sql, params) {
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
                const hasExplicitStatus = params.length >= 6;
                const task = {
                    task_id: params[0],
                    package_id: params[1],
                    machine_id: params[2],
                    task_type: params[3] || 'deploy',
                    status: hasExplicitStatus ? params[4] : 'pending',
                    scheduled_at: hasExplicitStatus ? params[5] : params[4],
                    started_at: null,
                    completed_at: null,
                    lease_expires_at: null,
                    error_message: null,
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
            if (sql.includes("t.status IN ('downloading', 'running')") || sql.includes("t.status = 'pending'")) {
                const hasDefaultTypeFilter = sql.includes("p.type IN ('software-deploy', 'file-deploy')");
                const hasSingleTypeFilter = sql.includes('p.type = $3');
                const packageTypeFilter = hasSingleTypeFilter ? params[2] : null;
                const claimable = Array.from(tasks.values())
                    .filter((task) => {
                        if (task.machine_id !== params[0]) {
                            return false;
                        }
                        if (task.scheduled_at && Date.parse(task.scheduled_at) > Date.now()) {
                            return false;
                        }
                        const pkg = packages.get(task.package_id);
                        const taskType = pkg?.type ?? 'software-deploy';
                        if (hasDefaultTypeFilter && taskType === 'game-option-deploy') {
                            return false;
                        }
                        if (hasSingleTypeFilter && taskType !== packageTypeFilter) {
                            return false;
                        }
                        if (task.status === 'pending') {
                            return true;
                        }
                        if ((task.status === 'downloading' || task.status === 'running') && task.lease_expires_at) {
                            return Date.parse(task.lease_expires_at) <= Date.now();
                        }
                        return false;
                    })
                    .map((task) => {
                        const pkg = packages.get(task.package_id);
                        return {
                            ...task,
                            name: pkg?.name ?? 'Test',
                            version: pkg?.version ?? '1.0',
                            type: pkg?.type ?? 'software-deploy',
                            file_size: pkg?.file_size ?? 1024,
                        };
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
                        if (sql.includes('started_at = COALESCE(started_at, NOW())')) {
                            task.started_at ??= new Date().toISOString();
                        }
                        if (sql.includes('completed_at = NOW()')) {
                            task.completed_at = new Date().toISOString();
                        }
                        if (sql.includes('lease_expires_at = NOW() +')) {
                            const leaseSeconds = Number(params[2]);
                            if (Number.isFinite(leaseSeconds)) {
                                task.lease_expires_at = new Date(Date.now() + (leaseSeconds * 1000)).toISOString();
                            }
                        }
                        if (sql.includes('lease_expires_at = NULL')) {
                            task.lease_expires_at = null;
                        }
                        if (sql.includes('error_message = NULL')) {
                            task.error_message = null;
                        }
                        if (params.length > 2) {
                            const lastParam = params[params.length - 1];
                            if (typeof lastParam === 'string' && !Number.isFinite(Number(lastParam)) && lastParam !== task.status) {
                                task.error_message = lastParam;
                            }
                        }
                    }
                }
                return { rows: [task].filter(Boolean) };
            }
            if (sql.includes('INSERT INTO deployment_tokens')) {
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
                const requiresUnused = sql.includes('used_at IS NULL');
                if (
                    token
                    && token.machine_id === params[1]
                    && token.package_id === params[2]
                    && token.resource_type === params[3]
                    && (!requiresUnused || !token.used_at)
                ) {
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

async function registerMachine(database, machineId, keyPair, options = {}) {
    const keyId = options.keyId || `test-key-${machineId}`;
    const machine = await database.upsertMachine(machineId, false, 'SDEZ');
    machine.approved = options.approved ?? true;
    machine.revoked = options.revoked ?? false;

    await database.updateMachineKey(machineId, {
        keyId,
        keyType: 'RSA',
        pubkeyPem: keyPair.publicKey.export({ type: 'spki', format: 'pem' }),
    });

    return { keyId };
}

function buildMachineRequestSigningPayload({
    machineId,
    keyId,
    method,
    path,
    host = '127.0.0.1',
    timestamp,
    nonce,
    body = '',
}) {
    const bodyHash = crypto.createHash('sha256').update(body, 'utf8').digest('hex');
    return [
        'VHDMountDeploymentRequestV1',
        String(machineId || '').trim(),
        String(keyId || '').trim(),
        String(method || '').trim().toUpperCase(),
        String(path || '').trim(),
        String(host || '').trim(),
        String(timestamp || '').trim(),
        String(nonce || '').trim(),
        bodyHash,
    ].join('\n');
}

function createMachineAuthHeaders({
    privateKey,
    machineId,
    keyId,
    method,
    path,
    host = '127.0.0.1',
    body = '',
}) {
    const timestamp = String(Date.now());
    const nonce = crypto.randomBytes(16).toString('hex');
    const payload = buildMachineRequestSigningPayload({
        machineId,
        keyId,
        method,
        path,
        host,
        timestamp,
        nonce,
        body,
    });
    const signature = crypto.sign('RSA-SHA256', Buffer.from(payload, 'utf8'), privateKey).toString('base64');
    return {
        'X-VHDM-KeyId': keyId,
        'X-VHDM-Timestamp': timestamp,
        'X-VHDM-Nonce': nonce,
        'X-VHDM-Signature': signature,
    };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-game-option-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();

    const { app, runtime } = await createApp({
        logger: { log: () => { }, error: () => { }, info: () => { } },
        database: fakeDatabase,
        configDir: tempDir,
        disableSignalHandlers: true,
    });

    const client = request.agent(app);
    const prepareResponse = await client
        .post('/api/init/prepare')
        .send({ issuer: 'VHDMountTest', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;
    await client
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SAFEBOOT',
        })
        .expect(201);

    const testKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const { keyId: testKeyId } = await registerMachine(fakeDatabase, 'machine-001', testKeyPair, {
        keyId: 'test-key-001',
        approved: true,
        revoked: false,
    });

    const otherKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const { keyId: otherKeyId } = await registerMachine(fakeDatabase, 'machine-002', otherKeyPair, {
        keyId: 'test-key-002',
        approved: true,
        revoked: false,
    });

    return { app, runtime, tempDir, database: fakeDatabase, testKeyPair, testKeyId, otherKeyPair, otherKeyId, totpSecret };
}

async function loginAndVerifyOtp(client, app) {
    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const runtime = app.locals.runtime;
    const secret = runtime.securityConfig.totpSecret;
    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(secret) })
        .expect(200);
}

async function uploadGameOptionPackage(client, fileContent = Buffer.from('pkg')) {
    const pkgPath = path.join(os.tmpdir(), `game-option-pkg-${Date.now()}.zip`);
    const sigPath = path.join(os.tmpdir(), `game-option-sig-${Date.now()}.sig`);
    fs.writeFileSync(pkgPath, fileContent);
    fs.writeFileSync(sigPath, 'signature');

    const res = await client
        .post('/api/deployments/packages')
        .field('name', 'GameOption')
        .field('version', '1.0.0')
        .field('type', 'game-option-deploy')
        .field('signer', 'test')
        .attach('package', pkgPath, 'package.zip')
        .attach('signature', sigPath, 'package.zip.sig')
        .expect(201);

    try { fs.unlinkSync(pkgPath); } catch { }
    try { fs.unlinkSync(sigPath); } catch { }

    return res.body.package.packageId;
}

test('GET /api/machines/:machineId/game-content/pending 需要机台签名认证', async (t) => {
    const { app } = await createInitializedHarness(t);
    const res = await request(app).get('/api/machines/machine-001/game-content/pending');
    assert.strictEqual(res.status, 401);
});

test('GET /api/machines/:machineId/game-content/pending 已审批签名机台返回空列表', async (t) => {
    const { app, testKeyPair, testKeyId } = await createInitializedHarness(t);
    const endpointPath = '/api/machines/machine-001/game-content/pending';
    const res = await request(app)
        .get(endpointPath)
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'GET',
            path: endpointPath,
        }));

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.success, true);
    assert.deepStrictEqual(res.body.tasks, []);
});

test('POST /api/deployments/packages 允许上传 game-option-deploy 类型包', async (t) => {
    const { app } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const packageId = await uploadGameOptionPackage(client);
    assert.match(packageId, /^pkg-[a-f0-9]{32}$/);

    const listRes = await client.get('/api/deployments/packages');
    assert.strictEqual(listRes.status, 200);
    const pkg = listRes.body.packages.find((p) => p.packageId === packageId);
    assert.ok(pkg);
    assert.strictEqual(pkg.type, 'game-option-deploy');
});

test('GET /api/machines/:machineId/game-content/pending 按类型过滤任务', async (t) => {
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const gamePkgId = await uploadGameOptionPackage(client);
    const normalPkgId = await (async () => {
        const pkgPath = path.join(os.tmpdir(), `normal-pkg-${Date.now()}.zip`);
        const sigPath = path.join(os.tmpdir(), `normal-sig-${Date.now()}.sig`);
        fs.writeFileSync(pkgPath, 'normal');
        fs.writeFileSync(sigPath, 'signature');
        const res = await client
            .post('/api/deployments/packages')
            .field('name', 'Normal')
            .field('version', '1.0.0')
            .field('type', 'software-deploy')
            .field('signer', 'test')
            .attach('package', pkgPath, 'package.zip')
            .attach('signature', sigPath, 'package.zip.sig')
            .expect(201);
        try { fs.unlinkSync(pkgPath); } catch { }
        try { fs.unlinkSync(sigPath); } catch { }
        return res.body.package.packageId;
    })();

    await client
        .post('/api/deployments/tasks')
        .send({ packageId: gamePkgId, targetMachineIds: ['machine-001'] })
        .expect(201);
    await client
        .post('/api/deployments/tasks')
        .send({ packageId: normalPkgId, targetMachineIds: ['machine-001'] })
        .expect(201);

    const endpointPath = '/api/machines/machine-001/game-content/pending';
    const res = await request(app)
        .get(endpointPath)
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'GET',
            path: endpointPath,
        }));

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.tasks.length, 1);
    assert.strictEqual(res.body.tasks[0].packageType, 'game-option-deploy');
});

test('GET /api/machines/:machineId/deployments/pending 不返回 game-option-deploy 任务', async (t) => {
    const { app, testKeyPair, testKeyId } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const gamePkgId = await uploadGameOptionPackage(client);
    await client
        .post('/api/deployments/tasks')
        .send({ packageId: gamePkgId, targetMachineIds: ['machine-001'] })
        .expect(201);

    const endpointPath = '/api/machines/machine-001/deployments/pending';
    const res = await request(app)
        .get(endpointPath)
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'GET',
            path: endpointPath,
        }));

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.tasks.length, 0);
});

test('game-content/pending 返回的 ZIP 与签名 AES 参数互相独立', async (t) => {
    const { app, testKeyPair, testKeyId } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const gamePkgId = await uploadGameOptionPackage(client);
    await client
        .post('/api/deployments/tasks')
        .send({ packageId: gamePkgId, targetMachineIds: ['machine-001'] })
        .expect(201);

    const endpointPath = '/api/machines/machine-001/game-content/pending';
    const res = await request(app)
        .get(endpointPath)
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'GET',
            path: endpointPath,
        }));

    assert.strictEqual(res.status, 200);
    assert.strictEqual(res.body.tasks.length, 1);
    const task = res.body.tasks[0];
    assert.ok(task.keyCipher);
    assert.ok(task.signatureKeyCipher);
    assert.notStrictEqual(task.keyCipher, task.signatureKeyCipher);
    assert.notStrictEqual(task.iv, task.signatureIv);
});

test('game-option-deploy 任务状态可复用现有状态接口迁移', async (t) => {
    const { app, database, testKeyPair, testKeyId } = await createInitializedHarness(t);
    const client = request.agent(app);
    await loginAndVerifyOtp(client, app);

    const gamePkgId = await uploadGameOptionPackage(client);
    const createRes = await client
        .post('/api/deployments/tasks')
        .send({ packageId: gamePkgId, targetMachineIds: ['machine-001'] })
        .expect(201);

    const taskId = createRes.body.tasks[0].taskId;

    // 先 claim 成 downloading
    const claimPath = '/api/machines/machine-001/game-content/pending';
    await request(app)
        .get(claimPath)
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'GET',
            path: claimPath,
        }))
        .expect(200);

    const runningPath = `/api/machines/machine-001/deployments/${taskId}/status`;
    const runningBody = JSON.stringify({ status: 'running' });
    const runningRes = await request(app)
        .post(runningPath)
        .set('Content-Type', 'application/json')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: runningPath,
            body: runningBody,
        }))
        .send(runningBody);

    assert.strictEqual(runningRes.status, 200);
    assert.strictEqual(runningRes.body.task.status, 'running');

    const successPath = `/api/machines/machine-001/deployments/${taskId}/status`;
    const successBody = JSON.stringify({ status: 'success' });
    const successRes = await request(app)
        .post(successPath)
        .set('Content-Type', 'application/json')
        .set(createMachineAuthHeaders({
            privateKey: testKeyPair.privateKey,
            machineId: 'machine-001',
            keyId: testKeyId,
            method: 'POST',
            path: successPath,
            body: successBody,
        }))
        .send(successBody);

    assert.strictEqual(successRes.status, 200);
    assert.strictEqual(successRes.body.task.status, 'success');
});
