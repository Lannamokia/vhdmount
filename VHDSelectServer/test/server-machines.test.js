const assert = require('node:assert/strict');
const crypto = require('crypto');
const test = require('node:test');

const { authenticator } = require('otplib');
const {
    buildSignedRegistrationRequest,
    createInitializedHarness,
    TEST_REGISTRATION_CERT_PEM,
} = require('./support/serverHarness');

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

    assert.ok(auditResponse.body.entries.every((entry) => entry.machineId === 'Machine-Mixed-01'));
});

test('机台注册必须使用可信证书签名且拒绝 nonce 重放', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);
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
    await client.post('/api/auth/otp/verify').send({ code: authenticator.generate(totpSecret) }).expect(200);
    await client.post(`/api/machines/${machineId}/approve`).send({ approved: true }).expect(200);
    await client
        .post(`/api/machines/${machineId}/evhd-password`)
        .send({ evhdPassword: 'EnvelopeSecret-456' })
        .expect(200);

    const bootstrapResponse = await client
        .get('/api/machine-log-bootstrap')
        .query({ machineId })
        .expect(200);

    assert.ok(bootstrapResponse.body.logChannelBootstrapId);

    const envelopeResponse = await client
        .get('/api/evhd-envelope')
        .query({ machineId })
        .expect(200);

    assert.ok(envelopeResponse.body.ciphertext);
    assert.notEqual(envelopeResponse.body.ciphertext, 'EnvelopeSecret-456');
});

test('已审批机台即使未配置 EVHD 密码也可获取日志 bootstrap', async (t) => {
    const { client, totpSecret } = await createInitializedHarness(t);
    const machineId = 'MACHINE-LOG-BOOT-ONLY';
    const keyId = 'VHDMounterKey_MACHINE-LOG-BOOT-ONLY';
    const keyType = 'RSA';
    const machineKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const pubkeyPem = machineKeyPair.publicKey.export({ type: 'spki', format: 'pem' });
    const signedRequest = buildSignedRegistrationRequest(machineId, keyId, keyType, pubkeyPem);

    await client
        .post(`/api/machines/${machineId}/keys`)
        .send(signedRequest)
        .expect(202);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    await client.post('/api/auth/otp/verify').send({ code: authenticator.generate(totpSecret) }).expect(200);
    await client.post(`/api/machines/${machineId}/approve`).send({ approved: true }).expect(200);

    const bootstrapResponse = await client
        .get('/api/machine-log-bootstrap')
        .query({ machineId })
        .expect(200);

    assert.equal(bootstrapResponse.body.machineId, machineId);

    await client
        .get('/api/evhd-envelope')
        .query({ machineId })
        .expect(404);
});

test('服务端会拒绝非法 machineId 输入', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .get('/api/boot-image-select')
        .query({ machineId: '../bad<script>' })
        .expect(400);

    assert.match(response.body.error, /machineId/);
});
