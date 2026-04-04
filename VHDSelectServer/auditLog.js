const fs = require('fs');
const path = require('path');

const { ensureWritableDirectory } = require('./configStoreUtils');

class AuditLog {
    constructor(filePath) {
        this.filePath = filePath;
        const dir = path.dirname(filePath);
        ensureWritableDirectory(dir);
    }

    append(entry) {
        const record = {
            ...entry,
            timestamp: entry.timestamp || new Date().toISOString(),
        };
        fs.appendFileSync(this.filePath, JSON.stringify(record) + '\n', 'utf8');
    }

    read({ type, limit = 100 } = {}) {
        if (!fs.existsSync(this.filePath)) {
            return [];
        }

        const rows = fs.readFileSync(this.filePath, 'utf8')
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

        const filtered = type ? rows.filter((row) => row.type === type) : rows;
        return filtered.slice(-Math.max(1, Math.min(limit, 500))).reverse();
    }
}

module.exports = {
    AuditLog,
};