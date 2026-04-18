const crypto = require('crypto');

class RegistrationAuthError extends Error {
    constructor(message, statusCode = 401) {
        super(message);
        this.name = 'RegistrationAuthError';
        this.statusCode = statusCode;
    }
}

function buildRegistrationSigningPayload({ machineId, keyId, keyType, pubkeyPem, timestamp, nonce }) {
    const canonicalMachineId = String(machineId || '').trim();
    const canonicalKeyId = String(keyId || '').trim();
    const canonicalKeyType = String(keyType || '').trim().toUpperCase();
    const canonicalPublicKeyPem = String(pubkeyPem || '').trim();
    const canonicalNonce = String(nonce || '').trim();
    const pubkeyHash = crypto.createHash('sha256').update(canonicalPublicKeyPem, 'utf8').digest('hex');
    return [
        'VHDMounterRegistrationV1',
        canonicalMachineId,
        canonicalKeyId,
        canonicalKeyType,
        pubkeyHash,
        String(timestamp),
        canonicalNonce,
    ].join('\n');
}

function buildMachineRequestSigningPayload({ method, path, machineId, keyId, timestamp, nonce, contentSha256, sessionId }) {
    return [
        'VHDMounterMachineRequestV1',
        String(method || '').trim().toUpperCase(),
        String(path || '').trim(),
        String(machineId || '').trim(),
        String(keyId || '').trim(),
        String(timestamp),
        String(nonce || '').trim(),
        String(contentSha256 || '').trim(),
        String(sessionId || '').trim(),
    ].join('\n');
}

function buildMachineLogHelloSigningPayload({
    protocolVersion,
    machineId,
    keyId,
    sessionId,
    bootstrapId,
    timestamp,
    nonce,
    clientEcdhPublicKey,
}) {
    return [
        'VHDMounterMachineLogHelloV1',
        String(protocolVersion || '').trim(),
        String(machineId || '').trim(),
        String(keyId || '').trim(),
        String(sessionId || '').trim(),
        String(bootstrapId || '').trim(),
        String(timestamp),
        String(nonce || '').trim(),
        String(clientEcdhPublicKey || '').trim(),
    ].join('\n');
}

function cleanupNonceCache(nonceCache, nowMs) {
    for (const [nonce, expiresAt] of nonceCache.entries()) {
        if (expiresAt <= nowMs) {
            nonceCache.delete(nonce);
        }
    }
}

function assertFreshTimestampAndNonce({ timestamp, nonce, nonceCache, now = new Date(), allowedSkewMs = 5 * 60 * 1000, nonceTtlMs = 10 * 60 * 1000, invalidTimestampMessage = 'timestamp 无效', expiredMessage = '请求已过期', duplicateMessage = '检测到重复请求' }) {
    if (!nonce || typeof nonce !== 'string' || nonce.trim().length < 16) {
        throw new RegistrationAuthError('nonce 无效');
    }

    const nowMs = now instanceof Date ? now.getTime() : new Date(now).getTime();
    const timestampMs = Number(timestamp);
    if (!Number.isFinite(timestampMs)) {
        throw new RegistrationAuthError(invalidTimestampMessage);
    }
    if (Math.abs(nowMs - timestampMs) > allowedSkewMs) {
        throw new RegistrationAuthError(expiredMessage);
    }

    cleanupNonceCache(nonceCache, nowMs);
    if (nonceCache.has(nonce)) {
        throw new RegistrationAuthError(duplicateMessage);
    }

    nonceCache.set(nonce, nowMs + nonceTtlMs);
    return timestampMs;
}

function verifyPemSignature({ payload, signature, publicKey, invalidSignatureMessage = '签名校验失败' }) {
    if (!signature || typeof signature !== 'string') {
        throw new RegistrationAuthError('缺少签名');
    }

    const verifier = crypto.createVerify('RSA-SHA256');
    verifier.update(payload);
    verifier.end();

    let signatureBytes;
    try {
        signatureBytes = Buffer.from(signature, 'base64');
    } catch {
        throw new RegistrationAuthError('签名格式无效');
    }

    const verified = verifier.verify(publicKey, signatureBytes);
    if (!verified) {
        throw new RegistrationAuthError(invalidSignatureMessage);
    }
}

function verifySignedRegistrationRequest({
    machineId,
    keyId,
    keyType,
    pubkeyPem,
    registrationCertificatePem,
    signature,
    timestamp,
    nonce,
    trustedCertificates,
    nonceCache,
    now = new Date(),
}) {
    const trusted = Array.isArray(trustedCertificates) ? trustedCertificates : [];
    if (!trusted.length) {
        throw new RegistrationAuthError('服务端尚未配置可信注册证书', 503);
    }

    if (!registrationCertificatePem || typeof registrationCertificatePem !== 'string') {
        throw new RegistrationAuthError('缺少 registrationCertificatePem');
    }
    const timestampMs = assertFreshTimestampAndNonce({
        timestamp,
        nonce,
        nonceCache,
        now,
        expiredMessage: '注册请求已过期',
        duplicateMessage: '检测到重复注册请求',
    });

    let certificate;
    try {
        certificate = new crypto.X509Certificate(registrationCertificatePem);
    } catch {
        throw new RegistrationAuthError('registrationCertificatePem 不是有效的 X.509 证书');
    }

    const fingerprint256 = certificate.fingerprint256.replace(/:/g, '').toUpperCase();
    const trustedCertificate = trusted.find((entry) => entry.fingerprint256 === fingerprint256);
    if (!trustedCertificate) {
        throw new RegistrationAuthError('注册证书不受信任');
    }

    const nowMs = now instanceof Date ? now.getTime() : new Date(now).getTime();
    const validFrom = new Date(certificate.validFrom).getTime();
    const validTo = new Date(certificate.validTo).getTime();
    if (Number.isFinite(validFrom) && nowMs < validFrom) {
        throw new RegistrationAuthError('注册证书尚未生效');
    }
    if (Number.isFinite(validTo) && nowMs > validTo) {
        throw new RegistrationAuthError('注册证书已过期');
    }

    const payload = buildRegistrationSigningPayload({
        machineId,
        keyId,
        keyType,
        pubkeyPem,
        timestamp: timestampMs,
        nonce,
    });

    verifyPemSignature({
        payload,
        signature,
        publicKey: certificate.publicKey,
        invalidSignatureMessage: '注册签名校验失败',
    });

    return {
        fingerprint256,
        subject: certificate.subject,
        trustedName: trustedCertificate.name,
    };
}

function verifySignedMachineRequest({
    method,
    path,
    machineId,
    keyId,
    publicKeyPem,
    signature,
    timestamp,
    nonce,
    contentSha256,
    sessionId,
    nonceCache,
    now = new Date(),
}) {
    const timestampMs = assertFreshTimestampAndNonce({
        timestamp,
        nonce,
        nonceCache,
        now,
        expiredMessage: '机台请求已过期',
        duplicateMessage: '检测到重复机台请求',
    });

    const payload = buildMachineRequestSigningPayload({
        method,
        path,
        machineId,
        keyId,
        timestamp: timestampMs,
        nonce,
        contentSha256,
        sessionId,
    });

    verifyPemSignature({
        payload,
        signature,
        publicKey: publicKeyPem,
        invalidSignatureMessage: '机台请求签名校验失败',
    });

    return { timestampMs };
}

function verifySignedMachineLogHello({
    protocolVersion,
    machineId,
    keyId,
    sessionId,
    bootstrapId,
    publicKeyPem,
    signature,
    timestamp,
    nonce,
    clientEcdhPublicKey,
    nonceCache,
    now = new Date(),
}) {
    const timestampMs = assertFreshTimestampAndNonce({
        timestamp,
        nonce,
        nonceCache,
        now,
        expiredMessage: '机台日志握手请求已过期',
        duplicateMessage: '检测到重复机台日志握手请求',
    });

    const payload = buildMachineLogHelloSigningPayload({
        protocolVersion,
        machineId,
        keyId,
        sessionId,
        bootstrapId,
        timestamp: timestampMs,
        nonce,
        clientEcdhPublicKey,
    });

    verifyPemSignature({
        payload,
        signature,
        publicKey: publicKeyPem,
        invalidSignatureMessage: '机台日志握手签名校验失败',
    });

    return { timestampMs };
}

module.exports = {
    RegistrationAuthError,
    assertFreshTimestampAndNonce,
    buildMachineLogHelloSigningPayload,
    buildMachineRequestSigningPayload,
    buildRegistrationSigningPayload,
    cleanupNonceCache,
    verifyPemSignature,
    verifySignedMachineLogHello,
    verifySignedMachineRequest,
    verifySignedRegistrationRequest,
};