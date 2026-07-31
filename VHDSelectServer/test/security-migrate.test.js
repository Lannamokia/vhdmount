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
            { id: 'key_1', name: '主认证器', secret: 'BASE32SECRET1', createdAt: '2026-01-01T00:00:00Z' },
            { id: 'key_2', name: '备用', secret: 'BASE32SECRET2', createdAt: '2026-01-02T00:00:00Z' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'BASE32SECRET1');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: totpSecret 已存在时只删 totpKeys', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpSecret: 'EXISTINGSECRET',
        totpKeys: [
            { id: 'key_1', name: '旧密钥', secret: 'OLDSECRET' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'EXISTINGSECRET');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: 无 totpKeys 时不做任何改动', () => {
    const store = createTempSecurityStore({ after() {} });
    const normalConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpSecret: 'NORMALSECRET',
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(normalConfig));

    const config = store.loadSecurityConfig();

    assert.equal(config.totpSecret, 'NORMALSECRET');
    assert.equal(config.totpKeys, undefined);
});

test('migrateLegacyTotpKeys: 迁移后写回文件', () => {
    const store = createTempSecurityStore({ after() {} });
    const legacyConfig = {
        version: 1,
        adminPasswordHash: 'hash',
        totpKeys: [
            { id: 'key_1', name: '认证器', secret: 'MIGRATETHIS' },
        ],
    };
    fs.writeFileSync(store.securityFile, JSON.stringify(legacyConfig));

    store.loadSecurityConfig();

    const written = JSON.parse(fs.readFileSync(store.securityFile, 'utf8'));
    assert.equal(written.totpSecret, 'MIGRATETHIS');
    assert.equal(written.totpKeys, undefined);
    assert.ok(written.updatedAt);
});
