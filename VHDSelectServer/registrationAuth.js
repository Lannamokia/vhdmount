const crypto = require('crypto');

class RegistrationAuthError extends Error {
    constructor(message, statusCode = 401) {
        super(message);
        this.name = 'RegistrationAuthError';
        this.statusCode = statusCode;
    }
}

function buildRegistrationSigningPayload({ machineId, keyId, keyType, pubkeyPem, timestamp, nonce }) {
    const canonicalMachineId = String(machineId || '').trim().toUpperCase();
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

function cleanupNonceCache(nonceCache, nowMs) {
    for (const [nonce, expiresAt] of nonceCache.entries()) {
        if (expiresAt <= nowMs) {
            nonceCache.delete(nonce);
        }
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
    if (!signature || typeof signature !== 'string') {
        throw new RegistrationAuthError('缺少签名');
    }
    if (!nonce || typeof nonce !== 'string' || nonce.trim().length < 16) {
        throw new RegistrationAuthError('nonce 无效');
    }

    const nowMs = now instanceof Date ? now.getTime() : new Date(now).getTime();
    const timestampMs = Number(timestamp);
    if (!Number.isFinite(timestampMs)) {
        throw new RegistrationAuthError('timestamp 无效');
    }
    if (Math.abs(nowMs - timestampMs) > 5 * 60 * 1000) {
        throw new RegistrationAuthError('注册请求已过期');
    }

    cleanupNonceCache(nonceCache, nowMs);
    if (nonceCache.has(nonce)) {
        throw new RegistrationAuthError('检测到重复注册请求');
    }

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

    const verifier = crypto.createVerify('RSA-SHA256');
    verifier.update(payload);
    verifier.end();

    let signatureBytes;
    try {
        signatureBytes = Buffer.from(signature, 'base64');
    } catch {
        throw new RegistrationAuthError('签名格式无效');
    }

    const verified = verifier.verify(certificate.publicKey, signatureBytes);
    if (!verified) {
        throw new RegistrationAuthError('注册签名校验失败');
    }

    nonceCache.set(nonce, nowMs + 10 * 60 * 1000);

    return {
        fingerprint256,
        subject: certificate.subject,
        trustedName: trustedCertificate.name,
    };
}

module.exports = {
    RegistrationAuthError,
    buildRegistrationSigningPayload,
    verifySignedRegistrationRequest,
};