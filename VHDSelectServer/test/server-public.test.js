const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { authenticator } = require('otplib');
const request = require('supertest');
const { createApp } = require('../server');
const { createInitializedHarness, TEST_REGISTRATION_CERT_PEM } = require('./support/serverHarness');

test('根路径返回 Flutter 客户端下载引导页', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-root-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => null,
        disableSignalHandlers: true,
    });

    const response = await request(app)
        .get('/')
        .expect(200);

    assert.match(response.text, /Flutter/);
});

test('首次初始化后不再接受默认密码登录', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'admin123456' })
        .expect(401);
});

test('带 Origin 的请求必须命中允许列表', async (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-origin-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = {
        async initialize() {},
        async close() {},
        async getMachineLogRuntimeSettings() {
            return {
                defaultRetentionActiveDays: 7,
                dailyInspectionHour: 3,
                dailyInspectionMinute: 0,
                timezone: 'UTC',
                lastInspectionAt: null,
            };
        },
        async updateMachineLogRuntimeSettings() {},
    };

    const { app } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
        disableSignalHandlers: true,
    });
    const client = request.agent(app);

    const prepareResponse = await client
        .post('/api/init/prepare')
        .send({ issuer: 'VHDMountTest', accountName: 'admin' })
        .expect(201);

    await client
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: '0123456789abcdef0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(prepareResponse.body.totpSecret),
            dbConfig: { host: 'localhost', port: 5432, database: 'test', user: 'test', password: 'test' },
            defaultVhdKeyword: 'SAFEBOOT',
            allowedOrigins: ['https://allowed.example.com'],
            trustedRegistrationCertificates: [{ name: 'test-registration-cert', certificatePem: TEST_REGISTRATION_CERT_PEM }],
        })
        .expect(201);

    await request(app)
        .get('/api/status')
        .set('Origin', 'https://blocked.example.com')
        .expect(403);

    await request(app)
        .get('/api/status')
        .set('Origin', 'https://allowed.example.com')
        .expect(200);
});

test('公开状态接口不会暴露敏感运行信息', async (t) => {
    const { client } = await createInitializedHarness(t);

    const response = await client
        .get('/api/status')
        .expect(200);

    assert.equal(response.body.success, true);
    assert.equal('defaultVhdKeyword' in response.body, false);
    assert.equal('databaseError' in response.body, false);
});

test('初始化状态接口仅在登录后返回管理端详情', async (t) => {
    const { client } = await createInitializedHarness(t);

    const anonymousResponse = await client
        .get('/api/init/status')
        .expect(200);

    assert.equal(anonymousResponse.body.initialized, true);
    assert.equal('defaultVhdKeyword' in anonymousResponse.body, false);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    const authenticatedResponse = await client
        .get('/api/init/status')
        .expect(200);

    assert.equal(authenticatedResponse.body.defaultVhdKeyword, 'SAFEBOOT');
    assert.equal(authenticatedResponse.body.trustedRegistrationCertificateCount, 1);
});

test('公开机台接口在数据库不可用时不会泄露内部错误详情', async (t) => {
    const { client, runtime } = await createInitializedHarness(t);

    runtime.database = null;
    runtime.databaseError = new Error('password authentication failed for user postgres');

    const response = await client
        .get('/api/boot-image-select')
        .query({ machineId: 'MACHINE-01' })
        .expect(503);

    assert.equal(response.body.error, '数据库当前不可用');
    assert.equal('details' in response.body, false);
});

test('默认不信任 X-Forwarded-For 头部', async (t) => {
    const { app, client } = await createInitializedHarness(t);

    await client
        .post('/api/auth/login')
        .send({ password: 'ComplexPassword123!' })
        .expect(200);

    await client
        .post('/api/machines')
        .set('X-Forwarded-For', '203.0.113.99')
        .send({ machineId: 'M-TRUST', vhdKeyword: 'SAFEBOOT', protected: false })
        .expect(201);

    const audit = await client
        .get('/api/audit')
        .expect(200);

    const machineCreate = audit.body.entries.find((entry) => entry.type === 'machine.create');
    assert.notEqual(machineCreate.ip, '203.0.113.99');
});
