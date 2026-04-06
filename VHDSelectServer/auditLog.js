const fs = require('fs');
const path = require('path');

const { ensureWritableDirectory } = require('./configStoreUtils');

const DEFAULT_MAX_BYTES = 5 * 1024 * 1024;
const DEFAULT_MAX_FILES = 5;

function normalizePositiveInteger(value, fallbackValue, minValue = 1, maxValue = Number.MAX_SAFE_INTEGER) {
    const parsed = Number.parseInt(String(value ?? ''), 10);
    if (!Number.isFinite(parsed) || parsed < minValue) {
        return fallbackValue;
    }
    return Math.min(parsed, maxValue);
}

class AuditLog {
    constructor(filePath, options = {}) {
        this.filePath = filePath;
        const dir = path.dirname(filePath);
        ensureWritableDirectory(dir);
        this.maxBytes = normalizePositiveInteger(
            options.maxBytes ?? process.env.AUDIT_LOG_MAX_BYTES,
            DEFAULT_MAX_BYTES,
        );
        this.maxFiles = normalizePositiveInteger(
            options.maxFiles ?? process.env.AUDIT_LOG_MAX_FILES,
            DEFAULT_MAX_FILES,
            1,
            20,
        );
    }

    append(entry) {
        const record = {
            ...entry,
            timestamp: entry.timestamp || new Date().toISOString(),
        };
        const line = JSON.stringify(record) + '\n';
        this.rotateIfNeeded(Buffer.byteLength(line, 'utf8'));
        fs.appendFileSync(this.filePath, line, 'utf8');
    }

    read({ type, limit = 100 } = {}) {
        const rows = this.getLogFilesOldestToNewest()
            .flatMap((filePath) => this.readRowsFromFile(filePath));

        const filtered = type ? rows.filter((row) => row.type === type) : rows;
        return filtered.slice(-Math.max(1, Math.min(limit, 500))).reverse();
    }

    getLogFilesOldestToNewest() {
        const files = [];

        for (let index = this.maxFiles - 1; index >= 1; index -= 1) {
            const rotatedPath = `${this.filePath}.${index}`;
            if (fs.existsSync(rotatedPath)) {
                files.push(rotatedPath);
            }
        }

        if (fs.existsSync(this.filePath)) {
            files.push(this.filePath);
        }

        return files;
    }

    readRowsFromFile(filePath) {
        return fs.readFileSync(filePath, 'utf8')
            .split(/\r?\n/)
            .map((line) => line.trim())
            .filter(Boolean)
            .map((line) => {
                try {
                    return JSON.parse(line);
                } catch {
                    return null;
                }
            })
            .filter(Boolean);
    }

    rotateIfNeeded(incomingBytes) {
        if (!fs.existsSync(this.filePath)) {
            return;
        }

        const currentSize = fs.statSync(this.filePath).size;
        if (currentSize + incomingBytes <= this.maxBytes) {
            return;
        }

        const oldestRotatedPath = `${this.filePath}.${this.maxFiles - 1}`;
        if (this.maxFiles > 1 && fs.existsSync(oldestRotatedPath)) {
            fs.unlinkSync(oldestRotatedPath);
        }

        for (let index = this.maxFiles - 1; index >= 2; index -= 1) {
            const sourcePath = `${this.filePath}.${index - 1}`;
            const targetPath = `${this.filePath}.${index}`;
            if (fs.existsSync(sourcePath)) {
                fs.renameSync(sourcePath, targetPath);
            }
        }

        if (this.maxFiles > 1) {
            fs.renameSync(this.filePath, `${this.filePath}.1`);
            return;
        }

        fs.truncateSync(this.filePath, 0);
    }
}

module.exports = {
    AuditLog,
};