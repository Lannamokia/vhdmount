const assert = require('node:assert/strict');
const test = require('node:test');

const { authenticator } = require('otplib');
const { createInitializedHarness } = require('./support/serverHarness');

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
