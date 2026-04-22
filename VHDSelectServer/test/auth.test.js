const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');
const { createApp } = require('../server');

function createFakeDatabase() {
    return {
        async initialize() { },
        async close() { },
        async getMachine() { return null; },
        async upsertMachine(machineId, data) {
            return {
                machine_id: machineId,
                protected: data.protected ?? false,
                vhd_keyword: data.vhd_keyword ?? 'SDEZ',
                evhd_password_configured: false,
            };
        },
        async getAllMachines() { return []; },
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
        async updateMachineProtection() { return null; },
        async updateMachineVhdKeyword() { return null; },
        async getMachineEvhdPassword() { return null; },
        async updateMachineEvhdPassword() { return null; },
        async deleteMachine() { return null; },
        async updateMachineLastSeen() { return null; },
        async updateMachineKey() { return null; },
        async approveMachine() { return null; },
        async revokeMachineKey() { return null; },
        async updateMachineLogRetentionOverride() { return null; },
        async getMachineLogAcknowledgedSeq() { return 0; },
        async persistMachineLogBatch() { },
        async getMachineLogSessions() { return []; },
        async getMachineLogs() { return { entries: [], nextCursor: null, hasMore: false }; },
        async exportMachineLogs() { return []; },
        async runMachineLogRetentionInspection() { },
    };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-auth-test-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
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

    return { app, client, totpSecret };
}

test('POST /api/auth/change-password rejects wrong current password', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const response = await client
        .post('/api/auth/change-password')
        .send({
            currentPassword: 'WrongPassword!',
            newPassword: 'NewPassword456!',
            confirmPassword: 'NewPassword456!',
        })
        .expect(401);

    assert.equal(response.body.success, false);
});

test('POST /api/auth/change-password rejects short new password', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const response = await client
        .post('/api/auth/change-password')
        .send({
            currentPassword: 'ComplexPassword123!',
            newPassword: 'short',
            confirmPassword: 'short',
        })
        .expect(400);

    assert.equal(response.body.success, false);
});

test('POST /api/auth/change-password rejects mismatched confirm', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const response = await client
        .post('/api/auth/change-password')
        .send({
            currentPassword: 'ComplexPassword123!',
            newPassword: 'NewPassword456!',
            confirmPassword: 'DifferentPassword!',
        })
        .expect(400);

    assert.equal(response.body.success, false);
});

test('POST /api/auth/change-password succeeds with valid input', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const response = await client
        .post('/api/auth/change-password')
        .send({
            currentPassword: 'ComplexPassword123!',
            newPassword: 'NewPassword456!',
            confirmPassword: 'NewPassword456!',
        })
        .expect(200);

    assert.equal(response.body.success, true);
});

test('POST /api/auth/change-password clears OTP verification', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    await client
        .post('/api/auth/change-password')
        .send({
            currentPassword: 'ComplexPassword123!',
            newPassword: 'NewPassword456!',
            confirmPassword: 'NewPassword456!',
        })
        .expect(200);

    const check = await client
        .get('/api/auth/otp/status')
        .expect(200);

    assert.equal(check.body.otpVerified, false);
});

test('POST /api/auth/logout destroys session', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/logout')
        .expect(200);

    const response = await client
        .get('/api/machines')
        .expect(401);

    assert.equal(response.body.requireAuth, true);
});

test('GET /api/auth/check returns correct states', async (t) => {
    const { client } = await createInitializedHarness(t);

    const beforeLogin = await client
        .get('/api/auth/check')
        .expect(200);

    assert.equal(beforeLogin.body.initialized, true);
    assert.equal(beforeLogin.body.isAuthenticated, false);
    assert.equal(beforeLogin.body.otpVerified, false);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const afterLogin = await client
        .get('/api/auth/check')
        .expect(200);

    assert.equal(afterLogin.body.initialized, true);
    assert.equal(afterLogin.body.isAuthenticated, true);
    assert.equal(afterLogin.body.otpVerified, false);
});

test('POST /api/auth/login rejects wrong password', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .post('/api/auth/login')
        .send({ password: 'WrongPassword!' })
        .expect(401);

    assert.equal(response.body.success, false);
});

test('POST /api/auth/login rejects default password after init', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .post('/api/auth/login')
        .send({ password: 'admin' })
        .expect(401);

    assert.equal(response.body.success, false);
});
