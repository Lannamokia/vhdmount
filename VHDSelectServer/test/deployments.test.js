const assert = require('node:assert/strict');
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
            };
            machines.set(machineId, machine);
            return machine;
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
                const token = {
                    token: params[0],
                    task_id: params[1],
                    machine_id: params[2],
                    package_id: params[3],
                    resource_type: params[4],
                    expires_at: params[5],
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

    return { app, runtime, tempDir, database: fakeDatabase };
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
