const assert = require('node:assert/strict');
const test = require('node:test');

const {
    ValidationError,
    assertCursor,
    assertKeyId,
    assertLogComponent,
    assertLogEventKey,
    assertLogLevel,
    assertMachineId,
    assertOptionalReason,
    assertOptionalIsoDate,
    assertOptionalPositiveInteger,
    assertRsaPublicKeyPem,
    assertSessionId,
    assertString,
    assertVhdKeyword,
    normalizeLogComponent,
    normalizeLogEventKey,
    normalizeLogLevel,
    normalizeMachineId,
    normalizeVhdKeyword,
} = require('../validators');

test('assertMachineId 接受合法机台 ID', () => {
    assert.equal(assertMachineId('machine-01'), 'machine-01');
    assert.equal(assertMachineId('MACHINE_01'), 'MACHINE_01');
    assert.equal(assertMachineId('abc123'), 'abc123');
    assert.equal(assertMachineId('a'), 'a');
});

test('assertMachineId 拒绝非法机台 ID', () => {
    assert.throws(() => assertMachineId(''), ValidationError);
    assert.throws(() => assertMachineId('   '), ValidationError);
    assert.throws(() => assertMachineId('machine.01'), ValidationError);
    assert.throws(() => assertMachineId('machine 01'), ValidationError);
    assert.throws(() => assertMachineId('a'.repeat(65)), ValidationError);
});

test('assertKeyId 接受合法密钥 ID', () => {
    assert.equal(assertKeyId('key-01_v2.0'), 'key-01_v2.0');
    assert.equal(assertKeyId('VHDMounterKey:test'), 'VHDMounterKey:test');
});

test('assertKeyId 拒绝非法密钥 ID', () => {
    assert.throws(() => assertKeyId(''), ValidationError);
    assert.throws(() => assertKeyId('key/01'), ValidationError);
    assert.throws(() => assertKeyId('a'.repeat(129)), ValidationError);
});

test('assertVhdKeyword 接受合法关键词并转大写', () => {
    assert.equal(assertVhdKeyword('SDEZ'), 'SDEZ');
    assert.equal(assertVhdKeyword('safeboot'), 'SAFEBOOT');
    assert.equal(assertVhdKeyword('  sdez  '), 'SDEZ');
    assert.equal(assertVhdKeyword('BOOT_1'), 'BOOT_1');
});

test('assertVhdKeyword 拒绝非法关键词', () => {
    assert.throws(() => assertVhdKeyword(''), ValidationError);
    assert.throws(() => assertVhdKeyword('boot.exe'), ValidationError);
    assert.throws(() => assertVhdKeyword('boot 1'), ValidationError);
    assert.throws(() => assertVhdKeyword('a'.repeat(65)), ValidationError);
});

test('normalizeVhdKeyword 转大写并去除空白', () => {
    assert.equal(normalizeVhdKeyword('sdez'), 'SDEZ');
    assert.equal(normalizeVhdKeyword('  SafeBoot  '), 'SAFEBOOT');
});

test('assertLogLevel 接受有效日志级别', () => {
    assert.equal(assertLogLevel('debug'), 'debug');
    assert.equal(assertLogLevel('INFO'), 'info');
    assert.equal(assertLogLevel('  Warn  '), 'warn');
    assert.equal(assertLogLevel('ERROR'), 'error');
});

test('assertLogLevel 拒绝无效日志级别', () => {
    assert.throws(() => assertLogLevel('fatal'), ValidationError);
    assert.throws(() => assertLogLevel(''), ValidationError);
    assert.throws(() => assertLogLevel('infox'), ValidationError);
});

test('normalizeLogLevel 无效值回退为 info', () => {
    assert.equal(normalizeLogLevel('debug'), 'debug');
    assert.equal(normalizeLogLevel('fatal'), 'info');
    assert.equal(normalizeLogLevel(''), 'info');
});

test('assertLogComponent 接受合法组件名', () => {
    assert.equal(assertLogComponent('VHDManager'), 'VHDManager');
    assert.equal(assertLogComponent('MainWindow.UI'), 'MainWindow.UI');
    assert.equal(assertLogComponent('app:service/v1'), 'app:service/v1');
});

test('assertLogComponent 拒绝非法组件名', () => {
    assert.throws(() => assertLogComponent(''), ValidationError);
    assert.throws(() => assertLogComponent('comp@nent'), ValidationError);
    assert.throws(() => assertLogComponent('a'.repeat(129)), ValidationError);
});

test('normalizeLogComponent 去除空白', () => {
    assert.equal(normalizeLogComponent('  VHDManager  '), 'VHDManager');
});

test('assertLogEventKey 接受合法事件键并转大写', () => {
    assert.equal(assertLogEventKey('MOUNT_START'), 'MOUNT_START');
    assert.equal(assertLogEventKey('mount_start'), 'MOUNT_START');
    assert.equal(assertLogEventKey('UI.BUTTON.CLICK'), 'UI.BUTTON.CLICK');
});

test('assertLogEventKey 拒绝非法事件键', () => {
    assert.throws(() => assertLogEventKey(''), ValidationError);
    assert.throws(() => assertLogEventKey('event key'), ValidationError);
    assert.throws(() => assertLogEventKey('event@key'), ValidationError);
    assert.throws(() => assertLogEventKey('a'.repeat(129)), ValidationError);
});

test('normalizeLogEventKey 转大写并去除空白', () => {
    assert.equal(normalizeLogEventKey('mount_start'), 'MOUNT_START');
});

test('assertSessionId 接受合法会话 ID', () => {
    assert.equal(assertSessionId('sess-01.v2'), 'sess-01.v2');
    assert.equal(assertSessionId('20260420T010203Z-abc'), '20260420T010203Z-abc');
});

test('assertSessionId 拒绝非法会话 ID', () => {
    assert.throws(() => assertSessionId(''), ValidationError);
    assert.throws(() => assertSessionId('sess/01'), ValidationError);
    assert.throws(() => assertSessionId('a'.repeat(129)), ValidationError);
});

test('assertString 校验字符串长度', () => {
    assert.equal(assertString('hello', 'name'), 'hello');
    assert.equal(assertString('  hello  ', 'name'), 'hello');
});

test('assertString 拒绝长度超限字符串', () => {
    assert.throws(() => assertString('', 'name'), ValidationError);
    assert.throws(() => assertString('a'.repeat(4097), 'name'), ValidationError);
    assert.throws(() => assertString('ab', 'name', 3, 10), ValidationError);
});

test('assertOptionalReason 校验查询原因', () => {
    assert.equal(assertOptionalReason('调试用途'), '调试用途');
    assert.equal(assertOptionalReason('  管理员查看  '), '管理员查看');
});

test('assertOptionalReason 拒绝过短或过长原因', () => {
    assert.throws(() => assertOptionalReason(''), ValidationError);
    assert.throws(() => assertOptionalReason('abc'), ValidationError);
    assert.throws(() => assertOptionalReason('a'.repeat(201)), ValidationError);
});

test('assertRsaPublicKeyPem 接受有效 PEM', () => {
    const pem = '-----BEGIN PUBLIC KEY-----\nMIIB...\n-----END PUBLIC KEY-----';
    assert.equal(assertRsaPublicKeyPem(pem), pem);
});

test('assertRsaPublicKeyPem 拒绝无效 PEM', () => {
    assert.throws(() => assertRsaPublicKeyPem('not-a-pem'), ValidationError);
    assert.throws(() => assertRsaPublicKeyPem(''), ValidationError);
});

test('assertCursor 接受合法游标', () => {
    assert.equal(assertCursor('eyJpZCI6MX0'), 'eyJpZCI6MX0');
});

test('assertCursor 拒绝非法游标', () => {
    assert.throws(() => assertCursor(''), ValidationError);
    assert.throws(() => assertCursor('a'.repeat(513)), ValidationError);
});

test('assertOptionalPositiveInteger 接受有效正整数', () => {
    assert.equal(assertOptionalPositiveInteger(7, 'days'), 7);
    assert.equal(assertOptionalPositiveInteger('30', 'days'), 30);
    assert.equal(assertOptionalPositiveInteger(3650, 'days'), 3650);
});

test('assertOptionalPositiveInteger 空值返回 null', () => {
    assert.equal(assertOptionalPositiveInteger(null, 'days'), null);
    assert.equal(assertOptionalPositiveInteger('', 'days'), null);
});

test('assertOptionalPositiveInteger 拒绝无效值', () => {
    assert.throws(() => assertOptionalPositiveInteger(0, 'days'), ValidationError);
    assert.throws(() => assertOptionalPositiveInteger(-1, 'days'), ValidationError);
    assert.throws(() => assertOptionalPositiveInteger(3651, 'days'), ValidationError);
    assert.throws(() => assertOptionalPositiveInteger('abc', 'days'), ValidationError);
});

test('assertOptionalIsoDate 接受有效 ISO 日期', () => {
    assert.equal(assertOptionalIsoDate('2026-04-20T12:00:00Z', 'from'), '2026-04-20T12:00:00.000Z');
    assert.equal(assertOptionalIsoDate('2026-04-20', 'from'), '2026-04-20T00:00:00.000Z');
});

test('assertOptionalIsoDate 空值返回 null', () => {
    assert.equal(assertOptionalIsoDate(null, 'from'), null);
    assert.equal(assertOptionalIsoDate('', 'from'), null);
});

test('assertOptionalIsoDate 拒绝无效日期', () => {
    assert.throws(() => assertOptionalIsoDate('not-a-date', 'from'), ValidationError);
    assert.throws(() => assertOptionalIsoDate('2026-13-01', 'from'), ValidationError);
});

test('normalizeMachineId 去除空白', () => {
    assert.equal(normalizeMachineId('  machine-01  '), 'machine-01');
    assert.equal(normalizeMachineId(null), '');
});

test('ValidationError 包含正确的状态码', () => {
    const error = new ValidationError('test error');
    assert.equal(error.message, 'test error');
    assert.equal(error.statusCode, 400);
    assert.equal(error.name, 'ValidationError');
});

test('ValidationError 支持自定义状态码', () => {
    const error = new ValidationError('not found', 404);
    assert.equal(error.statusCode, 404);
});
