const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');
const { createApp } = require('../server');

function createFakeDatabase() {
    const machines = new Map();
    let nextId = 1;

    function nowIso() {
        return new Date().toISOString();
    }

    function createRecord(machineId, overrides = {}) {
        const timestamp = nowIso();
        return {
            id: nextId++,
            machine_id: machineId,
            protected: false,
            vhd_keyword: 'SDEZ',
            evhd_password: null,
            key_id: null,
            key_type: null,
            pubkey_pem: null,
            approved: false,
            approved_at: null,
            revoked: false,
            revoked_at: null,
            last_seen: null,
            registration_cert_fingerprint: null,
            registration_cert_subject: null,
            log_retention_active_days_override: null,
            created_at: timestamp,
            updated_at: timestamp,
            ...overrides,
            evhd_password_configured: Boolean(overrides.evhd_password),
        };
    }

    return {
        async initialize() { },
        async close() { },
        async getMachine(machineId) {
            const record = machines.get(machineId);
            return record ? { ...record } : null;
        },
        async upsertMachine(machineId, data) {
            const record = createRecord(machineId, {
                protected: data.protected ?? false,
                vhd_keyword: data.vhd_keyword ?? 'SDEZ',
                evhd_password: data.evhd_password ?? null,
            });
            machines.set(machineId, record);
            return { ...record };
        },
        async getAllMachines() {
            return Array.from(machines.values()).map(r => ({ ...r }));
        },
        async updateMachineProtection(machineId, protectedState) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.protected = protectedState;
            record.updated_at = nowIso();
            return { ...record };
        },
        async updateMachineVhdKeyword(machineId, keyword) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.vhd_keyword = keyword;
            record.updated_at = nowIso();
            return { ...record };
        },
        async getMachineEvhdPassword(machineId) {
            const record = machines.get(machineId);
            return record?.evhd_password ?? null;
        },
        async updateMachineEvhdPassword(machineId, password) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.evhd_password = password;
            record.updated_at = nowIso();
            return { ...record, evhd_password_configured: Boolean(password) };
        },
        async deleteMachine(machineId) {
            const record = machines.get(machineId);
            if (!record) return null;
            machines.delete(machineId);
            return { ...record };
        },
        async updateMachineLastSeen(machineId) {
            const record = machines.get(machineId);
            if (record) {
                record.last_seen = nowIso();
            }
            return record ? { ...record } : null;
        },
        async updateMachineKey() { return null; },
        async approveMachine(machineId, approved) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.approved = approved;
            record.approved_at = approved ? nowIso() : null;
            record.updated_at = nowIso();
            return { ...record };
        },
        async revokeMachineKey(machineId) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.key_id = null;
            record.key_type = null;
            record.pubkey_pem = null;
            record.approved = false;
            record.approved_at = null;
            record.revoked = true;
            record.revoked_at = nowIso();
            record.updated_at = nowIso();
            return { ...record };
        },
        async updateMachineLogRetentionOverride(machineId, days) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.log_retention_active_days_override = days;
            record.updated_at = nowIso();
            return { ...record };
        },
        async getMachineLogRuntimeSettings() {
            return {
                defaultRetentionActiveDays: 7,
                dailyInspectionHour: 3,
                dailyInspectionMinute: 0,
                timezone: 'UTC',
                lastInspectionAt: null,
            };
        },
        async updateMachineLogRuntimeSettings() { },
        async getMachineLogAcknowledgedSeq() { return 0; },
        async persistMachineLogBatch() { },
        async getMachineLogSessions() { return []; },
        async getMachineLogs(filters) {
            return { entries: [], nextCursor: null, hasMore: false };
        },
        async exportMachineLogs() { return []; },
        async runMachineLogRetentionInspection() { },
    };
}

async function createUninitializedApp(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-middleware-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const { app } = await createApp({ configDir: tempDir });
    return { app, tempDir };
}

async function createInitializedApp(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-middleware-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
    });
    return { app, tempDir, fakeDatabase };
}

test('requireInitialized 未初始化时返回 503', async (t) => {
    const { app } = await createUninitializedApp(t);

    const response = await request(app)
        .get('/api/boot-image-select')
        .query({ machineId: 'test-machine' })
        .expect(503);

    assert.equal(response.body.success, false);
    assert.equal(response.body.initializeRequired, true);
});

test('requireInitialized 已初始化时通过', async (t) => {
    const { app } = await createInitializedApp(t);

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    const response = await request(app)
        .get('/api/health')
        .expect(200);

    assert.equal(response.body.initialized, true);
});

test('requireAuth 未初始化时返回 503', async (t) => {
    const { app } = await createUninitializedApp(t);

    const response = await request(app)
        .get('/api/machines')
        .expect(503);

    assert.equal(response.body.success, false);
    assert.equal(response.body.initializeRequired, true);
});

test('requireAuth 未登录时返回 401', async (t) => {
    const { app } = await createInitializedApp(t);

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    const response = await request(app)
        .get('/api/machines')
        .expect(401);

    assert.equal(response.body.success, false);
    assert.equal(response.body.requireAuth, true);
});

test('requireAuth 登录后通过', async (t) => {
    const { app } = await createInitializedApp(t);

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    const agent = request.agent(app);
    await agent
        .post('/api/auth/login')
        .send({ password: 'TestPass123!' })
        .expect(200);

    await agent
        .get('/api/auth/check')
        .expect(200);
});

test('requireOtpStepUp 未验证 OTP 时返回 403', async (t) => {
    const { app } = await createInitializedApp(t);

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    const agent = request.agent(app);
    await agent
        .post('/api/auth/login')
        .send({ password: 'TestPass123!' })
        .expect(200);

    const response = await agent
        .get('/api/security/trusted-certificates')
        .expect(403);

    assert.equal(response.body.success, false);
    assert.equal(response.body.requireOtp, true);
});

test('requireDatabase 数据库不可用时返回 503', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-middleware-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app, runtime } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
    });

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    runtime.database = null;

    const agent = request.agent(app);
    await agent
        .post('/api/auth/login')
        .send({ password: 'TestPass123!' })
        .expect(200);

    const response = await agent
        .get('/api/machines')
        .expect(503);

    assert.equal(response.body.success, false);
    assert.ok(response.body.error.includes('数据库'));
});

test('POST /api/auth/logout 销毁会话', async (t) => {
    const { app } = await createInitializedApp(t);

    const prepareResponse = await request(app)
        .post('/api/init/prepare')
        .send({ issuer: 'Test', accountName: 'admin' })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;

    await request(app)
        .post('/api/init/complete')
        .send({
            adminPassword: 'TestPass123!',
            sessionSecret: '0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SDEZ',
        })
        .expect(201);

    const agent = request.agent(app);
    await agent
        .post('/api/auth/login')
        .send({ password: 'TestPass123!' })
        .expect(200);

    await agent
        .post('/api/auth/logout')
        .expect(200);

    const response = await agent
        .get('/api/machines')
        .expect(401);

    assert.equal(response.body.requireAuth, true);
});

test('GET /api/auth/check 返回正确状态', async (t) => {
    const { app } = await createUninitializedApp(t);

    const response = await request(app)
        .get('/api/auth/check')
        .expect(200);

    assert.equal(response.body.initialized, false);
    assert.equal(response.body.isAuthenticated, false);
});
