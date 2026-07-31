const bcrypt = require('bcryptjs');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { authenticator } = require('otplib');

const { ensureWritableDirectory, writeJsonAtomic } = require('./configStoreUtils');
const { normalizeDbConfig } = require('./database');

function generateKeyId() {
    return 'key_' + crypto.randomBytes(8).toString('hex');
}

function normalizeOrigins(origins) {
    if (!Array.isArray(origins)) {
        return [];
    }

    return [...new Set(origins
        .map((origin) => String(origin || '').trim())
        .filter(Boolean))];
}

function normalizeTrustedRegistrationCertificate(input) {
    if (!input) {
        throw new Error('缺少证书信息');
    }

    const certificatePem = String(input.certificatePem || input.pem || '').trim();
    if (!certificatePem) {
        throw new Error('certificatePem 不能为空');
    }

    const certificate = new crypto.X509Certificate(certificatePem);
    return {
        name: String(input.name || certificate.subject || 'unnamed-certificate').trim(),
        certificatePem,
        fingerprint256: certificate.fingerprint256.replace(/:/g, '').toUpperCase(),
        subject: certificate.subject,
        validFrom: new Date(certificate.validFrom).toISOString(),
        validTo: new Date(certificate.validTo).toISOString(),
        addedAt: new Date().toISOString(),
    };
}

class SecurityStore {
    constructor(configDir = process.env.CONFIG_PATH || __dirname) {
        this.configDir = configDir;
        this.securityFile = path.join(configDir, 'server-security.json');
        this.lockFile = path.join(configDir, 'server-initialized.lock');
        this.pendingInitFile = path.join(configDir, 'server-pending-init.json');
        this.auditFile = path.join(configDir, 'server-audit.log');
        this.ensureConfigDir();
    }

    ensureConfigDir() {
        ensureWritableDirectory(this.configDir);
    }

    getPaths() {
        return {
            configDir: this.configDir,
            securityFile: this.securityFile,
            lockFile: this.lockFile,
            pendingInitFile: this.pendingInitFile,
            auditFile: this.auditFile,
        };
    }

    isInitialized() {
        return fs.existsSync(this.securityFile) && fs.existsSync(this.lockFile);
    }

    loadSecurityConfig() {
        if (!fs.existsSync(this.securityFile)) {
            throw new Error('安全配置文件不存在');
        }
        const config = JSON.parse(fs.readFileSync(this.securityFile, 'utf8'));
        if (this.migrateToMultiKey(config)) {
            this.saveSecurityConfig(config);
        }
        return config;
    }

    /**
     * 检测旧格式（存在 totpSecret 但无 totpKeys），自动迁移为多密钥数组。
     * 返回 true 表示发生了迁移，需要保存。
     */
    migrateToMultiKey(config) {
        if (config.totpKeys) {
            return false;
        }
        if (!config.totpSecret) {
            return false;
        }

        config.totpKeys = [{
            id: generateKeyId(),
            name: '初始认证器',
            type: 'authenticator',
            platform: null,
            secret: config.totpSecret,
            createdAt: config.initializedAt || new Date().toISOString(),
            lastUsedAt: null,
        }];

        delete config.totpSecret;
        return true;
    }

    saveSecurityConfig(config) {
        writeJsonAtomic(this.securityFile, config);
    }

    beginInitialization({ issuer = 'VHDMountServer', accountName = 'admin' } = {}) {
        const pending = {
            totpSecret: authenticator.generateSecret(),
            issuer: String(issuer || 'VHDMountServer').trim(),
            accountName: String(accountName || 'admin').trim(),
            createdAt: new Date().toISOString(),
        };
        writeJsonAtomic(this.pendingInitFile, pending);
        return {
            issuer: pending.issuer,
            accountName: pending.accountName,
            totpSecret: pending.totpSecret,
            otpauthUrl: authenticator.keyuri(pending.accountName, pending.issuer, pending.totpSecret),
        };
    }

    getPendingInitialization() {
        if (!fs.existsSync(this.pendingInitFile)) {
            return null;
        }
        return JSON.parse(fs.readFileSync(this.pendingInitFile, 'utf8'));
    }

    clearPendingInitialization() {
        if (fs.existsSync(this.pendingInitFile)) {
            fs.unlinkSync(this.pendingInitFile);
        }
    }

    buildSecurityConfig({
        adminPassword,
        sessionSecret,
        totpCode,
        dbConfig,
        allowedOrigins,
        trustedRegistrationCertificates,
    }) {
        const pending = this.getPendingInitialization();
        if (!pending) {
            throw new Error('未找到待完成的初始化会话，请先准备 OTP');
        }

        const normalizedPassword = String(adminPassword || '');
        if (normalizedPassword.length < 12) {
            throw new Error('管理员密码长度至少为 12 位');
        }

        const normalizedSecret = String(sessionSecret || '').trim();
        if (normalizedSecret.length < 32) {
            throw new Error('session secret 长度至少为 32 位');
        }

        const code = String(totpCode || '').trim();
        if (!authenticator.check(code, pending.totpSecret)) {
            throw new Error('OTP 验证失败');
        }

        const normalizedCertificates = Array.isArray(trustedRegistrationCertificates)
            ? trustedRegistrationCertificates.map((entry) => normalizeTrustedRegistrationCertificate(entry))
            : [];

        return {
            version: 1,
            initializedAt: new Date().toISOString(),
            sessionSecret: normalizedSecret,
            adminPasswordHash: bcrypt.hashSync(normalizedPassword, 12),
            totpKeys: [{
                id: generateKeyId(),
                name: '初始认证器',
                type: 'authenticator',
                platform: null,
                secret: pending.totpSecret,
                createdAt: new Date().toISOString(),
                lastUsedAt: null,
            }],
            totpIssuer: pending.issuer,
            totpAccountName: pending.accountName,
            allowedOrigins: normalizeOrigins(allowedOrigins),
            trustedRegistrationCertificates: normalizedCertificates,
            dbConfig: normalizeDbConfig(dbConfig),
        };
    }

    commitInitialization(config) {
        this.saveSecurityConfig(config);
        writeJsonAtomic(this.lockFile, {
            initializedAt: config.initializedAt,
            version: config.version,
        });
        this.clearPendingInitialization();
    }

    verifyPassword(password) {
        const config = this.loadSecurityConfig();
        return bcrypt.compareSync(String(password || ''), config.adminPasswordHash);
    }

    updatePassword(newPassword) {
        const config = this.loadSecurityConfig();
        config.adminPasswordHash = bcrypt.hashSync(String(newPassword || ''), 12);
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);
        return config;
    }

    verifyTotp(code) {
        const config = this.loadSecurityConfig();
        const keys = config.totpKeys || [];
        const normalizedCode = String(code || '').trim();
        for (const key of keys) {
            if (authenticator.check(normalizedCode, key.secret)) {
                key.lastUsedAt = new Date().toISOString();
                this.saveSecurityConfig(config);
                return { verified: true, keyId: key.id, keyType: key.type };
            }
        }
        return { verified: false };
    }

    createTotpBinding({ issuer, accountName, totpSecret } = {}) {
        const normalizedIssuer = String(issuer || '').trim() || 'VHDMountServer';
        const normalizedAccountName = String(accountName || '').trim() || 'admin';
        const secret = String(totpSecret || '').trim() || authenticator.generateSecret();

        return {
            issuer: normalizedIssuer,
            accountName: normalizedAccountName,
            totpSecret: secret,
            otpauthUrl: authenticator.keyuri(normalizedAccountName, normalizedIssuer, secret),
        };
    }

    verifyTotpWithSecret(code, totpSecret) {
        return authenticator.check(String(code || '').trim(), String(totpSecret || '').trim());
    }

    updateTotpBinding({ totpSecret, issuer, accountName }) {
        const config = this.loadSecurityConfig();
        const binding = this.createTotpBinding({
            issuer: issuer || config.totpIssuer,
            accountName: accountName || config.totpAccountName,
            totpSecret,
        });

        // OTP 轮换：替换所有现有密钥为一个新的 authenticator 类型密钥（全量重置）
        config.totpKeys = [{
            id: generateKeyId(),
            name: '初始认证器',
            type: 'authenticator',
            platform: null,
            secret: binding.totpSecret,
            createdAt: new Date().toISOString(),
            lastUsedAt: null,
        }];
        config.totpIssuer = binding.issuer;
        config.totpAccountName = binding.accountName;
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);

        return {
            ...binding,
            updatedAt: config.updatedAt,
        };
    }

    /**
     * 列出所有活跃 TOTP 密钥（不含 secret 字段）。
     */
    listTotpKeys() {
        const config = this.loadSecurityConfig();
        const keys = config.totpKeys || [];
        return keys.map(({ id, name, type, platform, createdAt, lastUsedAt }) => ({
            id,
            name,
            type,
            platform,
            createdAt,
            lastUsedAt,
        }));
    }

    /**
     * 注册新的 TOTP 密钥。
     * @param {{ name: string, type: 'authenticator' | 'biometric', platform?: string }} params
     * @returns {{ id, name, type, platform, secret, otpauthUrl, createdAt }}
     */
    addTotpKey({ name, type, platform }) {
        const config = this.loadSecurityConfig();
        const secret = authenticator.generateSecret();
        const id = generateKeyId();
        const createdAt = new Date().toISOString();
        const issuer = config.totpIssuer || 'VHDMountServer';
        const accountName = config.totpAccountName || 'admin';

        const newKey = {
            id,
            name: String(name || '').trim(),
            type,
            platform: platform || null,
            secret,
            createdAt,
            lastUsedAt: null,
        };

        if (!config.totpKeys) {
            config.totpKeys = [];
        }
        config.totpKeys.push(newKey);
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);

        return {
            id,
            name: newKey.name,
            type,
            platform: newKey.platform,
            secret,
            otpauthUrl: authenticator.keyuri(accountName, issuer, secret),
            createdAt,
        };
    }

    /**
     * 注销指定的 TOTP 密钥。
     * @param {string} keyId
     * @returns {{ success: boolean, error?: string }}
     */
    removeTotpKey(keyId) {
        const config = this.loadSecurityConfig();
        const keys = config.totpKeys || [];
        const index = keys.findIndex((key) => key.id === keyId);
        if (index === -1) {
            return { success: false, error: 'not_found' };
        }

        const targetKey = keys[index];
        if (targetKey.type === 'authenticator') {
            const authenticatorCount = keys.filter((key) => key.type === 'authenticator').length;
            if (authenticatorCount <= 1) {
                return { success: false, error: 'last_authenticator' };
            }
        }

        keys.splice(index, 1);
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);
        return { success: true };
    }

    listTrustedRegistrationCertificates() {
        const config = this.loadSecurityConfig();
        return config.trustedRegistrationCertificates || [];
    }

    addTrustedRegistrationCertificate(input) {
        const config = this.loadSecurityConfig();
        const entry = normalizeTrustedRegistrationCertificate(input);
        const next = (config.trustedRegistrationCertificates || []).filter((item) => item.fingerprint256 !== entry.fingerprint256);
        next.push(entry);
        config.trustedRegistrationCertificates = next;
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);
        return entry;
    }

    removeTrustedRegistrationCertificate(fingerprint) {
        const normalizedFingerprint = String(fingerprint || '').replace(/:/g, '').trim().toUpperCase();
        const config = this.loadSecurityConfig();
        const before = config.trustedRegistrationCertificates || [];
        const after = before.filter((entry) => entry.fingerprint256 !== normalizedFingerprint);
        if (before.length === after.length) {
            return false;
        }
        config.trustedRegistrationCertificates = after;
        config.updatedAt = new Date().toISOString();
        this.saveSecurityConfig(config);
        return true;
    }
}

module.exports = {
    SecurityStore,
    generateKeyId,
    normalizeOrigins,
    normalizeTrustedRegistrationCertificate,
};