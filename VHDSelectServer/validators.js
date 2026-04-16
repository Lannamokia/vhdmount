const MACHINE_ID_REGEX = /^[A-Za-z0-9_-]{1,64}$/;
const KEY_ID_REGEX = /^[A-Za-z0-9._:-]{1,128}$/;
const VHD_KEYWORD_REGEX = /^[A-Z0-9_-]{1,64}$/;

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

module.exports = {
    ValidationError,
    assertKeyId,
    assertMachineId,
    assertOptionalReason,
    assertRsaPublicKeyPem,
    assertString,
    assertVhdKeyword,
    normalizeMachineId,
    normalizeVhdKeyword,
};