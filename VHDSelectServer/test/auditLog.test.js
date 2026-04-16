const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { AuditLog } = require('../auditLog');

test('AuditLog rotates files when current audit log exceeds max size', () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-audit-'));
    const auditFile = path.join(tempDir, 'server-audit.log');

    try {
        const auditLog = new AuditLog(auditFile, {
            maxBytes: 260,
            maxFiles: 3,
        });

        for (let index = 1; index <= 6; index += 1) {
            auditLog.append({
                type: index % 2 === 0 ? 'auth' : 'machine',
                action: `action-${index}`,
                message: 'x'.repeat(30),
                timestamp: `2026-04-06T00:00:0${index}.000Z`,
            });
        }

        assert.equal(fs.existsSync(auditFile), true);
        assert.equal(fs.existsSync(`${auditFile}.1`), true);
        assert.equal(fs.existsSync(`${auditFile}.2`), true);
        assert.equal(fs.existsSync(`${auditFile}.3`), false);

        const recentEntries = auditLog.read({ limit: 6 });
        assert.deepEqual(
            recentEntries.map((entry) => entry.action),
            ['action-6', 'action-5', 'action-4', 'action-3', 'action-2', 'action-1'],
        );
    } finally {
        fs.rmSync(tempDir, { recursive: true, force: true });
    }
});

test('AuditLog read filters across rotated files', () => {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-audit-read-'));
    const auditFile = path.join(tempDir, 'server-audit.log');

    try {
        const auditLog = new AuditLog(auditFile, {
            maxBytes: 260,
            maxFiles: 3,
        });

        const entries = [
            { type: 'auth', action: 'login' },
            { type: 'machine', action: 'approve' },
            { type: 'auth', action: 'otp' },
            { type: 'security', action: 'add-cert' },
            { type: 'auth', action: 'logout' },
        ];

        entries.forEach((entry, index) => {
            auditLog.append({
                ...entry,
                detail: 'y'.repeat(28),
                timestamp: `2026-04-06T00:00:1${index}.000Z`,
            });
        });

        const authEntries = auditLog.read({ type: 'auth', limit: 10 });
        assert.deepEqual(
            authEntries.map((entry) => entry.action),
            ['logout', 'otp', 'login'],
        );
    } finally {
        fs.rmSync(tempDir, { recursive: true, force: true });
    }
});