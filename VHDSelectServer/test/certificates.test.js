const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');
const { createApp } = require('../server');

const TEST_REGISTRATION_CERT_PEM = `-----BEGIN CERTIFICATE-----
MIICzzCCAbegAwIBAgIJAPRk63P6tbNBMA0GCSqGSIb3DQEBCwUAMCcxJTAjBgNV
BAMTHFZIRE1vdW50IFRlc3QgUmVnaXN0cmF0aW9uIDIwHhcNMjYwNDAyMDgxOTU2
WhcNMjcwNDAzMDgxOTU2WjAnMSUwIwYDVQQDExxWSERMb3VudCBUZXN0IFJlZ2lz
dHJhdGlvbiAyMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqQ2RkyOZ
eN5wnvv1lXuISybOJFbjkSBnAorJj65LaKwooaptr3ySqeLSkxIA+5o6Rzgw+lh/
CAJhAcagfDAQ/2aH05dNUAoMO6smFJKFovslCBog7bk+vh+XoPs9jjsqT0gVzjH4
ncbq8e2diDrkcSdiRhSajxmrwnKywU3dKNqWm6NKyRQogf9f5kOJ//B7jVGC5yst
Mi3h9LHNWAXolich5vuOgZOrDxi5V5KDWXXsVVvtvwtVNg86cw83EHOayiU5uEEv
lmVmmLXjTC+0hKAXOsRMMfdkOFIdICmeLaiLRMAsM9ylxDOKB/n2FsbQLRF702jZ
CckgI7ncjew57QIDAQABMA0GCSqGSIb3DQEBCwUAA4IBAQAM0gG5dQ4dXTdxuWis
D5PMbJXGp6o6nqevH+RDR0Zk5vlbi7C/uhuOv/us9L+4YJbA3U5qHAKVevLZhC/A
Yv15STBGBP9dksHw2m0027tapgC3+GGzshbYZuYh5zIw2c9sNvdyA/tB4bJEKpae
NWV705ABr+DO/95GxssAmNdjJE4/cBmudREbx567NqHVn1q/dHvWM5jaVHxsSX8S
vYv27sFi6gn7xMgow34qN9arZkd5srj77GoQ/ycMClJsMVaRgmUqLuoei+QRlB64
6JYWszfC/N7haZFUpj31jjbs80Rw5JXrKo4XJ4T+BTiVwtaakK4rLu2En/beJpro
ZDNH
-----END CERTIFICATE-----`;

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
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-cert-test-'));
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
            trustedRegistrationCertificates: [
                { name: 'test-cert', certificatePem: TEST_REGISTRATION_CERT_PEM },
            ],
        })
        .expect(201);

    return { app, client, totpSecret };
}

test('GET /api/security/trusted-certificates requires OTP', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    const response = await client
        .get('/api/security/trusted-certificates')
        .expect(403);

    assert.equal(response.body.requireOtp, true);
});

test('GET /api/security/trusted-certificates returns list after OTP', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const response = await client
        .get('/api/security/trusted-certificates')
        .expect(200);

    assert.equal(response.body.success, true);
    assert.ok(Array.isArray(response.body.certificates));
    assert.equal(response.body.certificates.length, 1);
    assert.equal(response.body.certificates[0].name, 'test-cert');
});

test('POST /api/security/trusted-certificates adds certificate', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const response = await client
        .post('/api/security/trusted-certificates')
        .send({
            name: 'new-cert',
            certificatePem: TEST_REGISTRATION_CERT_PEM,
        })
        .expect(201);

    assert.equal(response.body.success, true);
    assert.equal(response.body.certificate.name, 'new-cert');

    const listResponse = await client
        .get('/api/security/trusted-certificates')
        .expect(200);

    assert.equal(listResponse.body.certificates.length, 1);
});

test('DELETE /api/security/trusted-certificates removes certificate', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const listBefore = await client
        .get('/api/security/trusted-certificates')
        .expect(200);

    const fingerprint = listBefore.body.certificates[0].fingerprint256;

    const response = await client
        .delete(`/api/security/trusted-certificates/${fingerprint}`)
        .expect(200);

    assert.equal(response.body.success, true);
    assert.equal(response.body.fingerprint256, fingerprint);

    const listAfter = await client
        .get('/api/security/trusted-certificates')
        .expect(200);

    assert.equal(listAfter.body.certificates.length, 0);
});

test('DELETE /api/security/trusted-certificates returns 404 for missing cert', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const response = await client
        .delete('/api/security/trusted-certificates/0000000000000000000000000000000000000000000000000000000000000000')
        .expect(404);

    assert.equal(response.body.success, false);
});

test('POST /api/security/trusted-certificates rejects invalid PEM', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const response = await client
        .post('/api/security/trusted-certificates')
        .send({
            name: 'bad-cert',
            certificatePem: 'not-a-valid-cert',
        })
        .expect(400);

    assert.equal(response.body.success, false);
});
