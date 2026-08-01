const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { SecurityStore } = require('../securityStore');

function createTempSecurityStore(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-security-migrate-'));
    t.after(() => fs.rmSync(tempDir, { recursive: true, force: true }));
    return new SecurityStore(tempDir);
}

test('migrateLegacyTotpKeys: totpKeys 存在且 totpSecret 缺失时自动迁移', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        sessionSecret: 'secret',
        totpKeys: [
            { id: 'key_1', name: '主认证器', secret: 'JBSWY3DPEHPK3PXP', createdAt: '2026-01-01T00:00:00Z' },
            { id: 'key_2', name: '备用', secret: 'MZXW6YTBON2GK3TU', createdAt: '2026-01-02T00:00:00Z' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'JBSWY3DPEHPK3PXP');
    assert.equal(config.totpKeys, undefined);
    assert.equal(fs.readdirSync(store.configDir).filter((name) => name.includes('.pre-migrate-')).length, 1);
});

test('migrateLegacyTotpKeys: totpSecret 已存在时只删 totpKeys', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpSecret: 'GEZDGNBVGY3TQOJQ',
        totpKeys: [
            { id: 'key_1', name: '旧密钥', secret: 'INVALID-SECRET' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'GEZDGNBVGY3TQOJQ');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: 跳过非法首项并选择第一个合法密钥', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpKeys: [
            { id: 'key_1', name: '损坏', secret: 'INVALID-SECRET' },
            { id: 'key_2', name: '备用', secret: 'KRUGS4ZANFZSAYJA' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'KRUGS4ZANFZSAYJA');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: 所有密钥非法时保留原配置并失败', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpKeys: [
            { id: 'key_1', name: '损坏', secret: 'INVALID-SECRET' },
            { id: 'key_2', name: '空值', secret: '' },
        ],
    };
    const original = JSON.stringify(legacyConfig);
    fs.writeFileSync(store.securityFile, original);

    assert.throws(() => store.loadSecurityConfig(), /不存在有效密钥/);
    assert.equal(fs.readFileSync(store.securityFile, 'utf8'), original);
    assert.equal(fs.readdirSync(store.configDir).filter((name) => name.includes('.pre-migrate-')).length, 0);
});

test('migrateLegacyTotpKeys: 空或畸形 totpKeys 且无合法密钥时失败并保留原配置', () => {
    for (const totpKeys of [[], { malformed: true }, null]) {
        const store = createTempSecurityStore({ after() {} });
        const legacyConfig = {
            version: 1,
            adminPasswordHash: 'hash',
            totpKeys,
        };
        const original = JSON.stringify(legacyConfig);
        fs.writeFileSync(store.securityFile, original);

        assert.throws(() => store.loadSecurityConfig(), /不存在有效密钥/);
        assert.equal(fs.readFileSync(store.securityFile, 'utf8'), original);
    }
});

test('migrateLegacyTotpKeys: 无 totpKeys 时不做任何改动', () => {
    const store = createTempSecurityStore({ after() {} });
    const normalConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpSecret: 'MZXW6YTBON2GK3TU',
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(normalConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'MZXW6YTBON2GK3TU');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: 迁移后写回文件', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpKeys: [
            { id: 'key_1', name: '认证器', secret: 'ONSWG4TFOR2XEZLQ' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    store.loadSecurityConfig();

    const written = JSON.parse(fs.readFileSync(store.securityFile, 'utf8'));
    assert.equal(written.totpSecret, 'ONSWG4TFOR2XEZLQ');
    assert.equal(written.totpKeys, undefined);
    assert.ok(written.updatedAt);
});
