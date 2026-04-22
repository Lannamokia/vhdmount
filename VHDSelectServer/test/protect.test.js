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
        };
    }

    return {
        async getMachine(machineId) {
            const record = machines.get(machineId);
            return record ? { ...record, evhd_password_configured: Boolean(record.evhd_password) } : null;
        },
        async upsertMachine(machineId, data) {
            const record = createRecord(machineId, {
                protected: data.protected ?? false,
                vhd_keyword: data.vhd_keyword ?? 'SDEZ',
                evhd_password: data.evhd_password ?? null,
            });
            machines.set(machineId, record);
            return { ...record, evhd_password_configured: Boolean(record.evhd_password) };
        },
        async updateMachineProtection(machineId, protected) {
            const record = machines.get(machineId);
            if (!record) return null;
            record.protected = protected;
            record.updated_at = nowIso();
            return { ...record, evhd_password_configured: Boolean(record.evhd_password) };
        },
        async getAllMachines() {
            return Array.from(machines.values()).map(r => ({ ...r, evhd_password_configured: Boolean(r.evhd_password) }));
        },
        async updateMachineLastSeen(machineId) {
            const record = machines.get(machineId);
            if (record) {
                record.last_seen = nowIso();
            }
            return record ? { ...record, evhd_password_configured: Boolean(record.evhd_password) } : null;
        },
    };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-protect-test-'));
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

    return { app, client, fakeDatabase, totpSecret };
}

test('GET /api/protect returns protection status', async (t) => {
    const { client, fakeDatabase } = await createInitializedHarness(t);

    await fakeDatabase.upsertMachine('M-01', { protected: true });

    const response = await client
        .get('/api/protect')
        .query({ machineId: 'M-01' })
        .expect(200);

    assert.equal(response.body.success, true);
    assert.equal(response.body.protected, true);
});

test('GET /api/protect returns false for unprotected machine', async (t) => {
    const { client, fakeDatabase } = await createInitializedHarness(t);

    await fakeDatabase.upsertMachine('M-02', { protected: false });

    const response = await client
        .get('/api/protect')
        .query({ machineId: 'M-02' })
        .expect(200);

    assert.equal(response.body.protected, false);
});

test('GET /api/protect returns 404 for unknown machine', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .get('/api/protect')
        .query({ machineId: 'UNKNOWN' })
        .expect(404);

    assert.equal(response.body.success, false);
});

test('POST /api/protect updates protection state', async (t) => {
    const { client, fakeDatabase } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await fakeDatabase.upsertMachine('M-03', { protected: false });

    const response = await client
        .post('/api/protect')
        .send({ machineId: 'M-03', protected: true })
        .expect(200);

    assert.equal(response.body.success, true);
    assert.equal(response.body.protected, true);
});

test('POST /api/protect rejects non-boolean protected', async (t) => {
    const { client, fakeDatabase } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await fakeDatabase.upsertMachine('M-04', { protected: false });

    const response = await client
        .post('/api/protect')
        .send({ machineId: 'M-04', protected: 'true' })
        .expect(400);

    assert.equal(response.body.success, false);
});

test('POST /api/protect requires authentication', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .post('/api/protect')
        .send({ machineId: 'M-05', protected: true })
        .expect(401);

    assert.equal(response.body.requireAuth, true);
});

test('GET /api/protect returns 404 for unknown machine', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .get('/api/protect')
        .query({ machineId: 'AUTO-CREATE-01' })
        .expect(404);

    assert.equal(response.body.success, false);
});
