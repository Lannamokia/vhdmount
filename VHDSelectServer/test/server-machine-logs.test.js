const assert = require('node:assert/strict');
const crypto = require('crypto');
const test = require('node:test');

const { authenticator } = require('otplib');
const {
    closeMachineLogTestServer,
    createInitializedHarness,
    createMachineLogTestServer,
    performMachineLogHandshake,
    registerApprovedMachine,
} = require('./support/serverHarness');

test('机台日志 WebSocket 握手按原始 ECDH shared secret 完成认证', async (t) => {
    const { app, client, runtime, totpSecret } = await createInitializedHarness(t);
    const server = await createMachineLogTestServer(app, runtime);
    t.after(async () => {
        await closeMachineLogTestServer(server);
    });

    const port = server.address().port;

    const rawMachineKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const rawMachineId = 'MACHINE-WS-RAW-01';
    const rawMachine = await registerApprovedMachine(client, rawMachineId, rawMachineKeyPair, totpSecret);
    await performMachineLogHandshake({
        port,
        machineId: rawMachineId,
        keyId: rawMachine.keyId,
        machineKeyPair: rawMachineKeyPair,
        bootstrap: rawMachine.bootstrap,
    });
});

test('机台日志 WebSocket 会拒绝非原始 ECDH shared secret 的 client_finish', async (t) => {
    const { app, client, runtime, totpSecret } = await createInitializedHarness(t);
    const server = await createMachineLogTestServer(app, runtime);
    t.after(async () => {
        await closeMachineLogTestServer(server);
    });

    const port = server.address().port;

    const machineKeyPair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
    const machineId = 'MACHINE-WS-RAW-FAIL-01';
    const machine = await registerApprovedMachine(client, machineId, machineKeyPair, totpSecret);
    await performMachineLogHandshake({
        port,
        machineId,
        keyId: machine.keyId,
        machineKeyPair,
        bootstrap: machine.bootstrap,
        useIncorrectSharedSecret: true,
    });
});

test('管理员可以配置全局与单机日志保留策略', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    await client
        .post('/api/machines')
        .send({
            machineId: 'MACHINE-RET-01',
            protected: false,
            vhdKeyword: 'SAFEBOOT',
        })
        .expect(201);

    const initialSettings = await client
        .get('/api/settings/log-retention')
        .expect(200);

    assert.equal(initialSettings.body.defaultRetentionActiveDays, 7);

    const updatedSettings = await client
        .post('/api/settings/log-retention')
        .send({
            defaultRetentionActiveDays: 30,
            dailyInspectionHour: 1,
            dailyInspectionMinute: 15,
            timezone: 'Asia/Shanghai',
        })
        .expect(200);

    assert.equal(updatedSettings.body.timezone, 'Asia/Shanghai');

    const machineOverride = await client
        .post('/api/machines/MACHINE-RET-01/log-retention')
        .send({ retentionActiveDaysOverride: 45 })
        .expect(200);

    assert.equal(machineOverride.body.retentionActiveDaysOverride, 45);
});

test('管理员更新日志保留策略时必须提供 IANA 时区', async (t) => {
    const { client } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);

    const response = await client
        .post('/api/settings/log-retention')
        .send({
            defaultRetentionActiveDays: 30,
            dailyInspectionHour: 1,
            dailyInspectionMinute: 15,
            timezone: 'China Standard Time',
        })
        .expect(400);

    assert.match(response.body.error, /IANA 时区/);
});

test('管理员可以按机台分页查询并导出机台日志', async (t) => {
    const { client, fakeDatabase, totpSecret } = await createInitializedHarness(t);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    await client
        .post('/api/machines')
        .send({
            machineId: 'MACHINE-LOG-01',
            protected: false,
            vhdKeyword: 'SAFEBOOT',
        })
        .expect(201);

    await fakeDatabase.persistMachineLogBatch({
        machineId: 'MACHINE-LOG-01',
        sessionId: '20260419T120300Z-7b1d2c',
        appVersion: '1.5.0',
        osVersion: 'Windows 11',
        uploadRequestId: 'req-01',
        entries: [
            {
                sessionId: '20260419T120300Z-7b1d2c',
                seq: 1,
                occurredAt: '2026-04-19T12:03:00.000Z',
                level: 'info',
                component: 'Program',
                eventKey: 'TRACE_LINE',
                message: 'boot complete',
                rawText: 'boot complete',
                metadata: { stage: 'boot' },
            },
            {
                sessionId: '20260419T120300Z-7b1d2c',
                seq: 2,
                occurredAt: '2026-04-19T12:04:00.000Z',
                level: 'warn',
                component: 'VHDManager',
                eventKey: 'EVHD_MOUNT_WAIT',
                message: 'waiting for mount',
                rawText: 'waiting for mount',
                metadata: { targetDrive: 'M:' },
            },
        ],
    });

    const sessionsResponse = await client
        .get('/api/machine-log-sessions')
        .query({ machineId: 'MACHINE-LOG-01' })
        .expect(200);

    assert.equal(sessionsResponse.body.count, 1);

    const firstPage = await client
        .get('/api/machine-logs')
        .query({ machineId: 'MACHINE-LOG-01', limit: 1 })
        .expect(200);

    assert.equal(firstPage.body.count, 1);
    assert.equal(firstPage.body.hasMore, true);

    await client
        .get('/api/machine-logs/export')
        .query({ machineId: 'MACHINE-LOG-01', format: 'text' })
        .expect(403);

    await client
        .post('/api/auth/otp/verify')
        .send({ code: authenticator.generate(totpSecret) })
        .expect(200);

    const exportResponse = await client
        .get('/api/machine-logs/export')
        .query({ machineId: 'MACHINE-LOG-01', format: 'text' })
        .expect(200);

    assert.match(exportResponse.text, /EVHD_MOUNT_WAIT/);
});
