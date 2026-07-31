const assert = require('node:assert/strict');
const { describe, test, beforeEach, afterEach } = require('node:test');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const { authenticator } = require('otplib');
const { SecurityStore, generateKeyId } = require('../securityStore');

/**
 * Helper: create a SecurityStore with a temp directory and seed it with a
 * minimal initialized security config containing the given totpKeys array.
 */
function createTestStore(t, { totpKeys, totpSecret } = {}) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-totp-multi-key-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const store = new SecurityStore(tempDir);

    // Build a minimal security config
    const secret = totpSecret || authenticator.generateSecret();
    const keys = totpKeys || [{
        id: generateKeyId(),
        name: '初始认证器',
        type: 'authenticator',
        platform: null,
        secret,
        createdAt: new Date().toISOString(),
        lastUsedAt: null,
    }];

    const config = {
        version: 1,
        initializedAt: new Date().toISOString(),
        sessionSecret: crypto.randomBytes(32).toString('hex'),
        adminPasswordHash: '$2a$12$fakehashfakehashfakehashfakehashfakehashfakehashfake',
        totpKeys: keys,
        totpIssuer: 'VHDMountTest',
        totpAccountName: 'admin',
        allowedOrigins: [],
        trustedRegistrationCertificates: [],
    };

    // Write config and lock file
    fs.writeFileSync(path.join(tempDir, 'server-security.json'), JSON.stringify(config, null, 2), 'utf8');
    fs.writeFileSync(path.join(tempDir, 'server-initialized.lock'), JSON.stringify({
        initializedAt: config.initializedAt,
        version: config.version,
    }), 'utf8');

    return { store, tempDir, secret, keys };
}

describe('多密钥验证遍历', () => {
    test('使用任一密钥生成的验证码均可通过验证', (t) => {
        const secret1 = authenticator.generateSecret();
        const secret2 = authenticator.generateSecret();
        const secret3 = authenticator.generateSecret();

        const keys = [
            { id: 'key_aaa', name: '认证器1', type: 'authenticator', platform: null, secret: secret1, createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_bbb', name: '生物识别', type: 'biometric', platform: 'windows-hello', secret: secret2, createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_ccc', name: '认证器2', type: 'authenticator', platform: null, secret: secret3, createdAt: new Date().toISOString(), lastUsedAt: null },
        ];

        const { store } = createTestStore(t, { totpKeys: keys });

        // Verify with first key
        const code1 = authenticator.generate(secret1);
        const result1 = store.verifyTotp(code1);
        assert.equal(result1.verified, true);
        assert.equal(result1.keyId, 'key_aaa');
        assert.equal(result1.keyType, 'authenticator');

        // Verify with second key (biometric)
        const code2 = authenticator.generate(secret2);
        const result2 = store.verifyTotp(code2);
        assert.equal(result2.verified, true);
        assert.equal(result2.keyId, 'key_bbb');
        assert.equal(result2.keyType, 'biometric');

        // Verify with third key
        const code3 = authenticator.generate(secret3);
        const result3 = store.verifyTotp(code3);
        assert.equal(result3.verified, true);
        assert.equal(result3.keyId, 'key_ccc');
        assert.equal(result3.keyType, 'authenticator');
    });

    test('无效验证码返回 verified: false', (t) => {
        const { store } = createTestStore(t);
        const result = store.verifyTotp('000000');
        assert.equal(result.verified, false);
        assert.equal(result.keyId, undefined);
    });
});

describe('密钥注册 (addTotpKey)', () => {
    test('注册新的 authenticator 密钥返回完整结构', (t) => {
        const { store } = createTestStore(t);

        const result = store.addTotpKey({
            name: '新认证器',
            type: 'authenticator',
            platform: null,
        });

        assert.ok(result.id, '应返回 id');
        assert.equal(result.name, '新认证器');
        assert.equal(result.type, 'authenticator');
        assert.equal(result.platform, null);
        assert.ok(result.secret, '应返回 secret');
        assert.ok(result.otpauthUrl, '应返回 otpauthUrl');
        assert.ok(result.createdAt, '应返回 createdAt');
    });

    test('注册新的 biometric 密钥包含 platform 字段', (t) => {
        const { store } = createTestStore(t);

        const result = store.addTotpKey({
            name: 'Windows Hello',
            type: 'biometric',
            platform: 'windows-hello',
        });

        assert.equal(result.type, 'biometric');
        assert.equal(result.platform, 'windows-hello');
    });

    test('注册的密钥被添加到配置中', (t) => {
        const { store } = createTestStore(t);

        const before = store.listTotpKeys();
        const initialCount = before.length;

        store.addTotpKey({ name: '额外密钥', type: 'authenticator' });

        const after = store.listTotpKeys();
        assert.equal(after.length, initialCount + 1);
        assert.ok(after.some((k) => k.name === '额外密钥'));
    });
});

describe('密钥注销 (removeTotpKey)', () => {
    test('成功注销指定密钥', (t) => {
        const keys = [
            { id: 'key_keep', name: '保留', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_remove', name: '删除', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        const result = store.removeTotpKey('key_remove');
        assert.equal(result.success, true);

        const remaining = store.listTotpKeys();
        assert.equal(remaining.length, 1);
        assert.equal(remaining[0].id, 'key_keep');
    });

    test('注销不存在的密钥返回 not_found', (t) => {
        const { store } = createTestStore(t);

        const result = store.removeTotpKey('key_nonexistent');
        assert.equal(result.success, false);
        assert.equal(result.error, 'not_found');
    });
});

describe('最后认证器保护', () => {
    test('仅剩一个 authenticator 密钥时拒绝注销', (t) => {
        const keys = [
            { id: 'key_auth', name: '唯一认证器', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_bio', name: '生物识别', type: 'biometric', platform: 'face-id', secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        const result = store.removeTotpKey('key_auth');
        assert.equal(result.success, false);
        assert.equal(result.error, 'last_authenticator');
    });

    test('有多个 authenticator 密钥时允许注销其中一个', (t) => {
        const keys = [
            { id: 'key_auth1', name: '认证器1', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_auth2', name: '认证器2', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        const result = store.removeTotpKey('key_auth1');
        assert.equal(result.success, true);
    });

    test('biometric 密钥可以自由注销即使只剩一个', (t) => {
        const keys = [
            { id: 'key_auth', name: '认证器', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_bio', name: '生物识别', type: 'biometric', platform: 'android-biometric', secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        const result = store.removeTotpKey('key_bio');
        assert.equal(result.success, true);
    });
});

describe('向后兼容迁移', () => {
    test('旧格式 totpSecret 自动迁移为 totpKeys 数组', (t) => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-totp-migrate-'));
        t.after(() => {
            fs.rmSync(tempDir, { recursive: true, force: true });
        });

        const oldSecret = authenticator.generateSecret();
        const oldConfig = {
            version: 1,
            initializedAt: '2024-01-15T10:00:00.000Z',
            sessionSecret: crypto.randomBytes(32).toString('hex'),
            adminPasswordHash: '$2a$12$fakehashfakehashfakehashfakehashfakehashfakehashfake',
            totpSecret: oldSecret,
            totpIssuer: 'VHDMountServer',
            totpAccountName: 'admin',
            allowedOrigins: [],
            trustedRegistrationCertificates: [],
        };

        fs.writeFileSync(path.join(tempDir, 'server-security.json'), JSON.stringify(oldConfig, null, 2), 'utf8');
        fs.writeFileSync(path.join(tempDir, 'server-initialized.lock'), JSON.stringify({
            initializedAt: oldConfig.initializedAt,
            version: oldConfig.version,
        }), 'utf8');

        const store = new SecurityStore(tempDir);
        const config = store.loadSecurityConfig();

        // Should have migrated
        assert.ok(Array.isArray(config.totpKeys), 'totpKeys 应为数组');
        assert.equal(config.totpKeys.length, 1);
        assert.equal(config.totpKeys[0].type, 'authenticator');
        assert.equal(config.totpKeys[0].name, '初始认证器');
        assert.equal(config.totpKeys[0].secret, oldSecret);
        assert.equal(config.totpKeys[0].createdAt, '2024-01-15T10:00:00.000Z');
        assert.equal(config.totpKeys[0].platform, null);
        assert.ok(config.totpKeys[0].id, '应生成 id');

        // Old field should be removed
        assert.equal(config.totpSecret, undefined);

        // Verification should still work with old secret
        const code = authenticator.generate(oldSecret);
        const result = store.verifyTotp(code);
        assert.equal(result.verified, true);
    });

    test('已有 totpKeys 的配置不触发迁移', (t) => {
        const { store } = createTestStore(t);
        const config = store.loadSecurityConfig();

        // Should not have totpSecret field
        assert.equal(config.totpSecret, undefined);
        assert.ok(Array.isArray(config.totpKeys));
    });
});

describe('OTP 轮换全量重置', () => {
    test('updateTotpBinding 替换所有密钥为单个新 authenticator 密钥', (t) => {
        const keys = [
            { id: 'key_1', name: '认证器1', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_2', name: '生物识别', type: 'biometric', platform: 'windows-hello', secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_3', name: '认证器2', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        const binding = store.updateTotpBinding({});

        // Should have exactly one key now
        const afterKeys = store.listTotpKeys();
        assert.equal(afterKeys.length, 1);
        assert.equal(afterKeys[0].type, 'authenticator');
        assert.equal(afterKeys[0].name, '初始认证器');

        // New secret should work
        const code = authenticator.generate(binding.totpSecret);
        const result = store.verifyTotp(code);
        assert.equal(result.verified, true);

        // Old secrets should no longer work
        for (const oldKey of keys) {
            const oldCode = authenticator.generate(oldKey.secret);
            const oldResult = store.verifyTotp(oldCode);
            assert.equal(oldResult.verified, false);
        }
    });
});

describe('platform 字段存储', () => {
    test('biometric 密钥的 platform 字段正确存储和返回', (t) => {
        const { store } = createTestStore(t);

        store.addTotpKey({
            name: 'Windows Hello 设备',
            type: 'biometric',
            platform: 'windows-hello',
        });

        const keys = store.listTotpKeys();
        const bioKey = keys.find((k) => k.name === 'Windows Hello 设备');
        assert.ok(bioKey, '应找到新添加的密钥');
        assert.equal(bioKey.platform, 'windows-hello');
        assert.equal(bioKey.type, 'biometric');
    });

    test('authenticator 密钥的 platform 为 null', (t) => {
        const { store } = createTestStore(t);

        store.addTotpKey({
            name: '新认证器',
            type: 'authenticator',
        });

        const keys = store.listTotpKeys();
        const authKey = keys.find((k) => k.name === '新认证器');
        assert.equal(authKey.platform, null);
    });

    test('各平台标识正确存储', (t) => {
        const { store } = createTestStore(t);

        const platforms = ['windows-hello', 'face-id', 'android-biometric'];
        for (const platform of platforms) {
            store.addTotpKey({
                name: `设备-${platform}`,
                type: 'biometric',
                platform,
            });
        }

        const keys = store.listTotpKeys();
        for (const platform of platforms) {
            const key = keys.find((k) => k.name === `设备-${platform}`);
            assert.ok(key, `应找到 platform=${platform} 的密钥`);
            assert.equal(key.platform, platform);
        }
    });
});

describe('lastUsedAt 验证后更新', () => {
    test('验证成功后更新匹配密钥的 lastUsedAt', (t) => {
        const secret = authenticator.generateSecret();
        const keys = [{
            id: 'key_track',
            name: '追踪密钥',
            type: 'authenticator',
            platform: null,
            secret,
            createdAt: new Date().toISOString(),
            lastUsedAt: null,
        }];
        const { store } = createTestStore(t, { totpKeys: keys });

        // Before verification, lastUsedAt is null
        const before = store.listTotpKeys();
        assert.equal(before[0].lastUsedAt, null);

        // Verify
        const code = authenticator.generate(secret);
        const result = store.verifyTotp(code);
        assert.equal(result.verified, true);

        // After verification, lastUsedAt should be set
        const after = store.listTotpKeys();
        assert.ok(after[0].lastUsedAt, 'lastUsedAt 应被更新');
        // Should be a valid ISO date
        assert.ok(!isNaN(new Date(after[0].lastUsedAt).getTime()));
    });
});

describe('Property 18: 服务端多密钥验证覆盖', () => {
    test('对于 N 个活跃密钥，任一密钥的有效验证码均可通过验证且 keyId 正确', (t) => {
        // Property-based: generate multiple keys and verify each one works
        const keyCount = 5;
        const keys = [];
        for (let i = 0; i < keyCount; i++) {
            keys.push({
                id: `key_prop_${i}`,
                name: `密钥${i}`,
                type: i % 2 === 0 ? 'authenticator' : 'biometric',
                platform: i % 2 === 0 ? null : 'windows-hello',
                secret: authenticator.generateSecret(),
                createdAt: new Date().toISOString(),
                lastUsedAt: null,
            });
        }

        const { store } = createTestStore(t, { totpKeys: keys });

        // Verify each key individually
        for (const key of keys) {
            const code = authenticator.generate(key.secret);
            const result = store.verifyTotp(code);
            assert.equal(result.verified, true, `密钥 ${key.id} 应验证成功`);
            assert.equal(result.keyId, key.id, `返回的 keyId 应为 ${key.id}`);
            assert.equal(result.keyType, key.type, `返回的 keyType 应为 ${key.type}`);
        }
    });
});

describe('Property 19: 最后一个认证器密钥不可注销', () => {
    test('无论 biometric 密钥数量如何，最后一个 authenticator 密钥始终受保护', (t) => {
        // Create 1 authenticator + N biometric keys
        const biometricCount = 3;
        const keys = [{
            id: 'key_sole_auth',
            name: '唯一认证器',
            type: 'authenticator',
            platform: null,
            secret: authenticator.generateSecret(),
            createdAt: new Date().toISOString(),
            lastUsedAt: null,
        }];

        for (let i = 0; i < biometricCount; i++) {
            keys.push({
                id: `key_bio_${i}`,
                name: `生物识别${i}`,
                type: 'biometric',
                platform: 'windows-hello',
                secret: authenticator.generateSecret(),
                createdAt: new Date().toISOString(),
                lastUsedAt: null,
            });
        }

        const { store } = createTestStore(t, { totpKeys: keys });

        // Attempting to remove the sole authenticator should fail
        const result = store.removeTotpKey('key_sole_auth');
        assert.equal(result.success, false);
        assert.equal(result.error, 'last_authenticator');

        // All biometric keys can be removed
        for (let i = 0; i < biometricCount; i++) {
            const bioResult = store.removeTotpKey(`key_bio_${i}`);
            assert.equal(bioResult.success, true, `biometric 密钥 ${i} 应可注销`);
        }

        // After removing all biometric keys, authenticator still protected
        const finalResult = store.removeTotpKey('key_sole_auth');
        assert.equal(finalResult.success, false);
        assert.equal(finalResult.error, 'last_authenticator');
    });

    test('有多个 authenticator 时可以逐个注销直到剩最后一个', (t) => {
        const keys = [
            { id: 'key_a1', name: '认证器A', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_a2', name: '认证器B', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
            { id: 'key_a3', name: '认证器C', type: 'authenticator', platform: null, secret: authenticator.generateSecret(), createdAt: new Date().toISOString(), lastUsedAt: null },
        ];
        const { store } = createTestStore(t, { totpKeys: keys });

        // Remove first two — should succeed
        assert.equal(store.removeTotpKey('key_a1').success, true);
        assert.equal(store.removeTotpKey('key_a2').success, true);

        // Third (last) should be protected
        const result = store.removeTotpKey('key_a3');
        assert.equal(result.success, false);
        assert.equal(result.error, 'last_authenticator');
    });
});

describe('listTotpKeys 不泄露 secret', () => {
    test('列表中不包含 secret 字段', (t) => {
        const { store } = createTestStore(t);
        store.addTotpKey({ name: '测试密钥', type: 'authenticator' });

        const keys = store.listTotpKeys();
        for (const key of keys) {
            assert.equal(key.secret, undefined, '列表不应包含 secret');
        }
    });
});
