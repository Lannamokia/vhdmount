const assert = require('node:assert/strict');
const test = require('node:test');

const {
    assertMachineLogTimeZone,
    formatLogDay,
    normalizeMachineLogRuntimeSettings,
    normalizeMachineLogTimeZone,
} = require('../database');

test('normalizeMachineLogTimeZone 仅接受 IANA 时区并回退到默认值', () => {
    assert.equal(normalizeMachineLogTimeZone('Asia/Shanghai'), 'Asia/Shanghai');
    assert.equal(normalizeMachineLogTimeZone('UTC'), 'UTC');
    assert.equal(normalizeMachineLogTimeZone('China Standard Time', 'UTC'), 'UTC');
    assert.equal(normalizeMachineLogTimeZone('', 'Asia/Shanghai'), 'Asia/Shanghai');
});

test('assertMachineLogTimeZone 会拒绝 Windows 时区 ID', () => {
    assert.throws(
        () => assertMachineLogTimeZone('China Standard Time'),
        /IANA 时区/,
    );
});

test('formatLogDay 按服务端统一时区归一化 occurredAt', () => {
    assert.equal(
        formatLogDay('2026-04-20T16:30:00.000Z', 'Asia/Shanghai'),
        '2026-04-21',
    );
    assert.equal(
        formatLogDay('2026-04-20T16:30:00.000Z', 'UTC'),
        '2026-04-20',
    );
});

test('normalizeMachineLogRuntimeSettings 会把非法时区值收敛到 UTC', () => {
    const settings = normalizeMachineLogRuntimeSettings({
        defaultRetentionActiveDays: 30,
        timezone: 'China Standard Time',
    });

    assert.equal(settings.defaultRetentionActiveDays, 30);
    assert.equal(settings.timezone, 'UTC');
});