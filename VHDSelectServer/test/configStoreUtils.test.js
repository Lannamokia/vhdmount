const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { writeJsonAtomic } = require('../configStoreUtils');

test('writeJsonAtomic reports bind mount guidance when temp file creation fails', (t) => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-config-utils-'));
    const targetFile = path.join(tempDir, 'server-security.json');
    const originalWriteFileSync = fs.writeFileSync;

    t.after(() => {
        fs.writeFileSync = originalWriteFileSync;
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    fs.writeFileSync = function patchedWriteFileSync(filePath, ...args) {
        if (String(filePath).endsWith('.tmp')) {
            const error = new Error('permission denied');
            error.code = 'EACCES';
            throw error;
        }
        return originalWriteFileSync.call(this, filePath, ...args);
    };

    assert.throws(
        () => writeJsonAtomic(targetFile, { ok: true }),
        (error) => {
            assert.match(error.message, /CONFIG_PATH/);
            assert.match(error.message, /nodejs 用户/);
            assert.equal(error.code, 'EACCES');
            return true;
        },
    );
});