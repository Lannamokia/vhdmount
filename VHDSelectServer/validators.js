const MACHINE_ID_REGEX = /^[A-Za-z0-9_-]{1,64}$/;
const KEY_ID_REGEX = /^[A-Za-z0-9._:-]{1,128}$/;
const VHD_KEYWORD_REGEX = /^[A-Z0-9_-]{1,64}$/;
const SESSION_ID_REGEX = /^[A-Za-z0-9._:-]{1,128}$/;
const LOG_COMPONENT_REGEX = /^[A-Za-z0-9._:/-]{1,128}$/;
const LOG_EVENT_KEY_REGEX = /^[A-Z0-9_./:-]{1,128}$/;
const LOG_LEVELS = new Set(['debug', 'info', 'warn', 'error']);

class ValidationError extends Error {
    constructor(message, statusCode = 400) {
        super(message);
        this.name = 'ValidationError';
        this.statusCode = statusCode;
    }
}

function normalizeMachineId(value) {
    return String(value || '').trim();
}

function normalizeKeyId(value) {
    return String(value || '').trim();
}

function normalizeVhdKeyword(value) {
    return String(value || '').trim().toUpperCase();
}

function normalizeLogLevel(value) {
    const level = String(value || '').trim().toLowerCase();
    return LOG_LEVELS.has(level) ? level : 'info';
}

function normalizeLogComponent(value) {
    return String(value || '').trim();
}

function normalizeLogEventKey(value) {
    return String(value || '').trim().toUpperCase();
}

function assertMachineId(value) {
    const machineId = normalizeMachineId(value);
    if (!MACHINE_ID_REGEX.test(machineId)) {
        throw new ValidationError('machineId 仅允许 1-64 位字母、数字、下划线和短横线');
    }
    return machineId;
}

function assertKeyId(value) {
    const keyId = normalizeKeyId(value);
    if (!KEY_ID_REGEX.test(keyId)) {
        throw new ValidationError('keyId 仅允许 1-128 位字母、数字、点、下划线、短横线和冒号');
    }
    return keyId;
}

function assertVhdKeyword(value) {
    const keyword = normalizeVhdKeyword(value);
    if (!VHD_KEYWORD_REGEX.test(keyword)) {
        throw new ValidationError('VHD 关键词仅允许 1-64 位大写字母、数字、下划线和短横线');
    }
    return keyword;
}

function assertString(value, fieldName, minLength = 1, maxLength = 4096) {
    const text = String(value || '').trim();
    if (text.length < minLength || text.length > maxLength) {
        throw new ValidationError(`${fieldName} 长度必须在 ${minLength}-${maxLength} 之间`);
    }
    return text;
}

function assertOptionalReason(value) {
    const text = String(value || '').trim();
    if (!text) {
        throw new ValidationError('查询原因不能为空');
    }
    if (text.length < 4 || text.length > 200) {
        throw new ValidationError('查询原因长度必须在 4-200 之间');
    }
    return text;
}

function assertRsaPublicKeyPem(value) {
    const pem = String(value || '').trim();
    if (!pem.includes('-----BEGIN PUBLIC KEY-----') || !pem.includes('-----END PUBLIC KEY-----')) {
        throw new ValidationError('pubkeyPem 必须是有效的 PEM 公钥');
    }
    return pem;
}

function assertSessionId(value) {
    const sessionId = String(value || '').trim();
    if (!SESSION_ID_REGEX.test(sessionId)) {
        throw new ValidationError('sessionId 仅允许 1-128 位字母、数字、点、下划线、短横线和冒号');
    }
    return sessionId;
}

function assertLogLevel(value) {
    const level = String(value || '').trim().toLowerCase();
    if (!LOG_LEVELS.has(level)) {
        throw new ValidationError('level 仅允许 debug、info、warn、error');
    }
    return level;
}

function assertLogComponent(value, fieldName = 'component') {
    const component = normalizeLogComponent(value);
    if (!LOG_COMPONENT_REGEX.test(component)) {
        throw new ValidationError(`${fieldName} 仅允许 1-128 位字母、数字、点、下划线、短横线、冒号和斜杠`);
    }
    return component;
}

function assertLogEventKey(value) {
    const eventKey = normalizeLogEventKey(value);
    if (!LOG_EVENT_KEY_REGEX.test(eventKey)) {
        throw new ValidationError('eventKey 仅允许 1-128 位大写字母、数字、下划线、点、短横线、冒号和斜杠');
    }
    return eventKey;
}

function assertOptionalIsoDate(value, fieldName) {
    if (value == null || String(value).trim() === '') {
        return null;
    }

    const text = String(value).trim();
    const parsed = Date.parse(text);
    if (!Number.isFinite(parsed)) {
        throw new ValidationError(`${fieldName} 必须是有效的 ISO 时间字符串`);
    }
    return new Date(parsed).toISOString();
}

function assertCursor(value) {
    const cursor = String(value || '').trim();
    if (!cursor || cursor.length > 512) {
        throw new ValidationError('cursor 长度必须在 1-512 之间');
    }
    return cursor;
}

function assertOptionalPositiveInteger(value, fieldName, maxValue = 3650) {
    if (value == null || String(value).trim() === '') {
        return null;
    }

    const parsed = Number.parseInt(String(value).trim(), 10);
    if (!Number.isFinite(parsed) || parsed <= 0 || parsed > maxValue) {
        throw new ValidationError(`${fieldName} 必须是 1-${maxValue} 之间的正整数`);
    }
    return parsed;
}

module.exports = {
    ValidationError,
    assertKeyId,
    assertCursor,
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
};