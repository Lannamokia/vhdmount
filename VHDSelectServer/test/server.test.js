const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');

const { buildRegistrationSigningPayload } = require('../registrationAuth');
const { createApp } = require('../server');

const TEST_REGISTRATION_CERT_PEM = `-----BEGIN CERTIFICATE-----
MIICzzCCAbegAwIBAgIJAPRk63P6tbNBMA0GCSqGSIb3DQEBCwUAMCcxJTAjBgNV
BAMTHFZIRE1vdW50IFRlc3QgUmVnaXN0cmF0aW9uIDIwHhcNMjYwNDAyMDgxOTU2
WhcNMjcwNDAzMDgxOTU2WjAnMSUwIwYDVQQDExxWSERNb3VudCBUZXN0IFJlZ2lz
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

const TEST_REGISTRATION_PRIVATE_KEY_PEM = `-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCpDZGTI5l43nCe
+/WVe4hLJs4kVuORIGcCismPrktorCihqm2vfJKp4tKTEgD7mjpHODD6WH8IAmEB
xqB8MBD/ZofTl01QCgw7qyYUkoWi+yUIGiDtuT6+H5eg+z2OOypPSBXOMfidxurx
7Z2IOuRxJ2JGFJqPGavCcrLBTd0o2pabo0rJFCiB/1/mQ4n/8HuNUYLnKy0yLeH0
sc1YBeiWJyHm+46Bk6sPGLlXkoNZdexVW+2/C1U2DzpzDzcQc5rKJTm4QS+WZWaY
teNML7SEoBc6xEwx92Q4Uh0gKZ4tqItEwCwz3KXEM4oH+fYWxtAtEXvTaNkJySAj
udyN7DntAgMBAAECggEARjV+ag9047/uIfkea3CckCmTn3/+jv1YCrQ9NdD7PIOT
dGDloOYpuyiar73gbp4E6iMqJC6ww1DJnQUzDaCgzpF0g6noz/78SaOw8wZPPfrz
zEOdvV0b87YHMTJmxDVKQxb7B2G1kUFVvhgjPrrGuT/UDqrr7daJgP5FwwZlfVtu
LebH5/ENtGH0nzeaGpPTtR018r6vBGA3D6s4/iGFnYrxNJXXwFDdhBX6DkuJdwSV
XgGASw53Sk+JbJZ3YOSJDrXWZPyXsPCfj2ywVq0HOKpEZQ0TkuTcHXbP2XmYc6ca
484qxfaOBY6VJMrxE9z240x/wQb7Ak9QIlaO4D2M+QKBgQDLaP+e+vgQuzVY0XA+
V4rp69cnNZLlds0IwvaAfQl1ysM9XWQG8E7hYShDpRHE/E6MuDnYov7nEm3vy2OZ
BPVJq6I7xeIWJGHpK9ePgjUFLSZv2KbgNapDGdd6Ut964tP3p+IpkrfZrB2FZ27J
uQ4Ax3bfBsZyT5L7wWA6CE+M4wKBgQDUwpdtSgJhpybx6GkJwsoS5ER5Jv0lv8mO
YjlUB0zZb8GVbClZkqepEiaT8WRlxvwHdaP4XGm2C/Bp3IMBKqBnLEERS61drXQl
EbUm7jcgOtl8gBrR7E7LR/LiASNecQCSFJvPArkfbRZs4k5u/MTq28y776dZJwfT
N7e8Oqom7wKBgGuET4FoJNErMzKEWfEJ8upcd7hI8CGMHypfa05VSTfS+kooYCPu
x7MH2PGQggj+WEK3ahQha90V97hFaJrMbR8IstMncK7Fgl9uhh1b9MyMpgF+og5n
L10Sfrwwq+HXnbUNL1VMMRPEj0IhfwTvZQByblnKygBIIWgjOcrS88GDAoGBAMWL
T/Inj4KAIsbllfF8LQfRbkpXCyDrrAdJ6BS/GnmhLErCvLnwUz/GHI+syB0/3m5G
qlJF69kdyMFh/zksDPb+vgODEpsyG+73PA3DjOed/KV+hGh5UseoLDnv+JkNrwvz
mp9g1eX58aJzlYOzqlqubq/o2qcKeFeDGlPo3Gd9AoGADuRvbd78duwXlGcAlZSR
45ZjPLwbBydW1kv6s5zYvTSkO7Y8nDrAhFM69WdwowEcY8iVJIoHtpAWoCvdRw8C
+yyeyZNIR7LHseOoD7pI/hZhg6GbFl3KJ7l+RAjZUIawMrzkSvS/VyJufdVTWakI
drSqfOcX13H9kw4eCCVv9Hg=
-----END PRIVATE KEY-----`;

function clone(value) {
    return value == null ? value : JSON.parse(JSON.stringify(value));
}

function createFakeDatabase() {
    const machines = new Map();
    const evhdPasswords = new Map();
    let nextId = 1;

    function nowIso() {
        return new Date().toISOString();
    }

    function syncPasswordFlag(machineId, record) {
        return {
            ...record,
            evhd_password_configured: evhdPasswords.has(machineId) && Boolean(evhdPasswords.get(machineId)),
        };
    }

    function createRecord(machineId, overrides = {}) {
        const timestamp = nowIso();
        return syncPasswordFlag(machineId, {
            id: nextId++,
            machine_id: machineId,
            protected: false,
            vhd_keyword: 'SDEZ',
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
            created_at: timestamp,
            updated_at: timestamp,
            ...overrides,
        });
    }

    function getRecord(machineId) {
        const record = machines.get(machineId);
        return record ? clone(syncPasswordFlag(machineId, record)) : null;
    }

    function saveRecord(machineId, nextRecord) {
        const record = syncPasswordFlag(machineId, {
            ...nextRecord,
            updated_at: nowIso(),
        });
        machines.set(machineId, record);
        return clone(record);
    }

    return {
        async initialize() {
            return undefined;
        },
        async close() {
            return undefined;
        },
        async getMachine(machineId) {
            return getRecord(machineId);
        },
        async upsertMachine(machineId, isProtected = false, vhdKeyword = 'SDEZ') {
            const current = machines.get(machineId);
            if (!current) {
                const created = createRecord(machineId, {
                    protected: isProtected,
                    vhd_keyword: vhdKeyword,
                });
                machines.set(machineId, created);
                return clone(created);
            }
            return saveRecord(machineId, {
                ...current,
                protected: isProtected,
                vhd_keyword: vhdKeyword,
            });
        },
        async updateMachineProtection(machineId, isProtected) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                protected: isProtected,
            });
        },
        async updateMachineVhdKeyword(machineId, vhdKeyword) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                vhd_keyword: vhdKeyword,
            });
        },
        async getMachineEvhdPassword(machineId) {
            return evhdPasswords.get(machineId) || null;
        },
        async updateMachineEvhdPassword(machineId, evhdPassword) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            evhdPasswords.set(machineId, evhdPassword);
            return saveRecord(machineId, current);
        },
        async getAllMachines() {
            return Array.from(machines.values())
                .sort((left, right) => left.machine_id.localeCompare(right.machine_id))
                .map((record) => clone(syncPasswordFlag(record.machine_id, record)));
        },
        async deleteMachine(machineId) {
            const existed = machines.delete(machineId);
            evhdPasswords.delete(machineId);
            return existed ? { machine_id: machineId } : null;
        },
        async updateMachineLastSeen(machineId) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            const timestamp = nowIso();
            machines.set(machineId, {
                ...current,
                last_seen: timestamp,
                updated_at: timestamp,
            });
            return timestamp;
        },
        async updateMachineKey(machineId, payload) {
            const current = machines.get(machineId) || createRecord(machineId);
            return saveRecord(machineId, {
                ...current,
                key_id: payload.keyId,
                key_type: payload.keyType,
                pubkey_pem: payload.pubkeyPem,
                approved: false,
                approved_at: null,
                revoked: false,
                revoked_at: null,
                registration_cert_fingerprint: payload.registrationCertFingerprint || null,
                registration_cert_subject: payload.registrationCertSubject || null,
            });
        },
        async approveMachine(machineId, approved) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                approved,
                approved_at: approved ? nowIso() : null,
            });
        },
        async revokeMachineKey(machineId) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                key_id: null,
                key_type: null,
                pubkey_pem: null,
                approved: false,
                approved_at: null,
                revoked: false,
                revoked_at: null,
                registration_cert_fingerprint: null,
                registration_cert_subject: null,
            });
        },
    };
}

function buildSignedRegistrationRequest(machineId, keyId, keyType, pubkeyPem, nonce = crypto.randomBytes(16).toString('hex')) {
    const timestamp = Date.now();
    const payload = buildRegistrationSigningPayload({
        machineId,
        keyId,
        keyType,
        pubkeyPem,
        timestamp,
        nonce,
    });
    const signer = crypto.createSign('RSA-SHA256');
    signer.update(payload);
    signer.end();

    return {
        keyId,
        keyType,
        pubkeyPem,
        registrationCertificatePem: TEST_REGISTRATION_CERT_PEM,
        signature: signer.sign(TEST_REGISTRATION_PRIVATE_KEY_PEM, 'base64'),
        timestamp,
        nonce,
    };
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app, runtime } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
    });
    const client = request.agent(app);

    const prepareResponse = await client
        .post('/api/init/prepare')
        .send({
            issuer: 'VHDMountTest',
            accountName: 'admin',
        })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;
    const completeResponse = await client
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: '0123456789abcdef0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: {
                host: 'localhost',
                port: 5432,
                database: 'vhd_select',
                user: 'tester',
                password: 'secret',
            },
            defaultVhdKeyword: 'SAFEBOOT',
            trustedRegistrationCertificates: [
                {
                    name: 'test-registration-cert',
                    certificatePem: TEST_REGISTRATION_CERT_PEM,
                },
            ],
        })
        .expect(201);

    assert.equal(completeResponse.body.initialized, true);

    return {
        app,
        client,
        fakeDatabase,
        runtime,
        tempDir,
        totpSecret,
    };
}

test('根路径返回 Flutter 客户端下载引导页', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-landing-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const { app } = await createApp({
        configDir: tempDir,
    });

    const response = await request(app)
        .get('/')
        .expect(200)
        .expect('Content-Type', /html/)
        .expect('Cache-Control', 'no-store');

    assert.match(response.text, /Flutter 管理入口导航/);
    assert.match(response.text, /https:\/\/github\.com\/Lannamokia\/vhdmount\/releases/);
    assert.match(response.text, /不要手动追加 \/api/);
});

test('首次初始化后不再接受默认密码登录', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-init-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
    });
    const anonymousClient = request(app);

    await anonymousClient
        .post('/api/auth/login')
        .send({ password: 'admin123' })
        .expect(503);

    const prepareResponse = await anonymousClient
        .post('/api/init/prepare')
        .send({ issuer: 'VHDMountTest', accountName: 'admin' })
        .expect(201);

    await anonymousClient
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: 'fedcba9876543210fedcba9876543210fedcba9876543210',
            totpCode: authenticator.generate(prepareResponse.body.totpSecret),
            dbConfig: {
                host: 'localhost',
                port: 5432,
                database: 'vhd_select',
                user: 'tester',
                password: 'secret',
            },
            trustedRegistrationCertificates: [
                {
                    name: 'test-registration-cert',
                    certificatePem: TEST_REGISTRATION_CERT_PEM,
                },
            ],
        })
        .expect(201);

    assert.equal(fs.existsSync(path.join(tempDir, 'server-security.json')), true);
    assert.equal(fs.existsSync(path.join(tempDir, 'server-initialized.lock')), true);

    const agent = request.agent(app);
    await agent.post('/api/auth/login').send({ password: 'admin123' }).expect(401);
    await agent.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
});

test('带 Origin 的请求必须命中允许列表', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-origin-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
    });
    const client = request(app);

    await client
        .options('/api/init/status')
        .set('Origin', 'https://evil.example')
        .expect(403);

    const prepareResponse = await client
        .post('/api/init/prepare')
        .send({ issuer: 'VHDMountTest', accountName: 'admin' })
        .expect(201);

    await client
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: '111122223333444455556666777788889999aaaabbbbcccc',
            totpCode: authenticator.generate(prepareResponse.body.totpSecret),
            dbConfig: {
                host: 'localhost',
                port: 5432,
                database: 'vhd_select',
                user: 'tester',
                password: 'secret',
            },
            allowedOrigins: ['https://admin.example'],
            trustedRegistrationCertificates: [
                {
                    name: 'test-registration-cert',
                    certificatePem: TEST_REGISTRATION_CERT_PEM,
                },
            ],
        })
        .expect(201);

    await client
        .options('/api/status')
        .set('Origin', 'https://admin.example')
        .expect(204)
        .expect('Access-Control-Allow-Origin', 'https://admin.example');

    await client
        .get('/api/status')
        .set('Origin', 'https://admin.example')
        .expect(200)
        .expect('Access-Control-Allow-Origin', 'https://admin.example');

    await client
        .get('/api/status')
        .set('Origin', 'https://evil.example')
        .expect(403);
});

test('查询 EVHD 明文前必须完成 OTP 二次验证', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    await client
        .post('/api/machines/MACHINE-01/evhd-password')
        .send({ evhdPassword: 'TopSecret-123' })
        .expect(200);

    await client
        .get('/api/evhd-password/plain')
        .query({ machineId: 'MACHINE-01', reason: 'support check' })
        .expect(403);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const plainResponse = await client
        .get('/api/evhd-password/plain')
        .query({ machineId: 'MACHINE-01', reason: 'support check' })
        .expect(200);

    assert.equal(plainResponse.body.evhdPassword, 'TopSecret-123');
});

test('OTP 二次验证有效期默认为 60 秒', async (t) => {
    const { client, runtime, totpSecret } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    const startedAt = Date.now();
    const otpResponse = await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);
    const finishedAt = Date.now();

    assert.equal(runtime.otpStepUpWindowMs, 60 * 1000);
    assert.ok(otpResponse.body.otpVerifiedUntil >= startedAt + runtime.otpStepUpWindowMs);
    assert.ok(otpResponse.body.otpVerifiedUntil <= finishedAt + runtime.otpStepUpWindowMs);
});

test('管理员可以先校验旧 OTP 再更换新的 OTP 绑定密钥', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    const prepareResponse = await client
        .post('/api/auth/otp/rotate/prepare')
        .send({
            currentCode: authenticator.generate(totpSecret),
            issuer: 'VHDMountRotated',
            accountName: 'security-admin',
        })
        .expect(200);

    assert.equal(prepareResponse.body.issuer, 'VHDMountRotated');
    assert.equal(prepareResponse.body.accountName, 'security-admin');
    assert.ok(prepareResponse.body.totpSecret);
    assert.ok(prepareResponse.body.otpauthUrl);

    await client
        .post('/api/auth/otp/rotate/complete')
        .send({ code: authenticator.generate(prepareResponse.body.totpSecret) })
        .expect(200);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(401);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(prepareResponse.body.totpSecret) })
        .expect(200);
});

test('管理员可以新增机台、切换保护状态并删除机台', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    const createResponse = await client
        .post('/api/machines')
        .send({
            machineId: 'MACHINE-ADD-01',
            protected: true,
            vhdKeyword: 'SAFEBOOT',
            evhdPassword: 'AddedSecret-789',
        })
        .expect(201);

    assert.equal(createResponse.body.machine.machine_id, 'MACHINE-ADD-01');
    assert.equal(createResponse.body.machine.protected, true);
    assert.equal(createResponse.body.machine.vhd_keyword, 'SAFEBOOT');
    assert.equal(createResponse.body.machine.evhd_password_configured, true);

    await client
        .post('/api/protect')
        .send({ machineId: 'MACHINE-ADD-01', protected: false })
        .expect(200);

    const detailResponse = await client
        .get('/api/machines/MACHINE-ADD-01')
        .expect(200);

    assert.equal(detailResponse.body.machine.protected, false);

    await client
        .delete('/api/machines/MACHINE-ADD-01')
        .expect(200);

    await client
        .get('/api/machines/MACHINE-ADD-01')
        .expect(404);
});

test('机台 ID 保留原始大小写并支持按机台筛选审计日志', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    await client
        .post('/api/machines')
        .send({
            machineId: 'Machine-Mixed-01',
            protected: false,
            vhdKeyword: 'SAFEBOOT',
        })
        .expect(201);

    await client
        .post('/api/machines')
        .send({
            machineId: 'Machine-Other-02',
            protected: true,
            vhdKeyword: 'OTHERBOOT',
        })
        .expect(201);

    const detailResponse = await client
        .get('/api/machines/Machine-Mixed-01')
        .expect(200);

    assert.equal(detailResponse.body.machine.machine_id, 'Machine-Mixed-01');

    const auditResponse = await client
        .get('/api/audit')
        .query({ machineId: 'Machine-Mixed-01', limit: 20 })
        .expect(200);

    assert.ok(auditResponse.body.entries.length >= 1);
    assert.ok(auditResponse.body.entries.every((entry) => entry.machineId === 'Machine-Mixed-01'));
});

test('机台注册必须使用可信证书签名且拒绝 nonce 重放', async (t) => {
    const { client } = await createInitializedHarness(t);
    const machineId = 'Machine-Reg';
    const keyId = 'key-01';
    const keyType = 'RSA';
    const machineKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const pubkeyPem = machineKeyPair.publicKey.export({ type: 'spki', format: 'pem' });
    const signedRequest = buildSignedRegistrationRequest(machineId, keyId, keyType, pubkeyPem);

    await client
        .post(`/api/machines/${machineId}/keys`)
        .send({ keyId, keyType, pubkeyPem })
        .expect(400);

    const registrationResponse = await client
        .post(`/api/machines/${machineId}/keys`)
        .send(signedRequest)
        .expect(202);

    const expectedFingerprint = new crypto.X509Certificate(TEST_REGISTRATION_CERT_PEM)
        .fingerprint256
        .replace(/:/g, '')
        .toUpperCase();

    assert.equal(registrationResponse.body.registrationCertFingerprint, expectedFingerprint);

    await client
        .post(`/api/machines/${machineId}/keys`)
        .send(signedRequest)
        .expect(401);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    await client.post(`/api/machines/${machineId}/approve`).send({ approved: true }).expect(200);
    await client
        .post(`/api/machines/${machineId}/evhd-password`)
        .send({ evhdPassword: 'EnvelopeSecret-456' })
        .expect(200);

    const envelopeResponse = await client
        .get('/api/evhd-envelope')
        .query({ machineId })
        .expect(200);

    assert.ok(envelopeResponse.body.ciphertext);
    assert.notEqual(envelopeResponse.body.ciphertext, 'EnvelopeSecret-456');
});

test('服务端会拒绝非法 machineId 输入', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .get('/api/boot-image-select')
        .query({ machineId: '../bad<script>' })
        .expect(400);

    assert.match(response.body.error, /machineId/);
});