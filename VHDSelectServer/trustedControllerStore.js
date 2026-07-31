'use strict';

const crypto = require('crypto');

/**
 * 可信 RustDesk 主控端列表 + watermark（决策点 6）。
 * 写操作原子 ++ snapshotVersion 并持久化到 trusted_rustdesk_controllers_watermark；
 * 进程启动时通过 ensureLoaded() 从表恢复 watermark。
 */
class TrustedControllerStore {
    constructor(database, logger = console) {
        this.database = database;
        this.logger = logger;
        this._snapshotVersion = 0;
        this._loaded = false;
    }

    async ensureLoaded() {
        if (this._loaded) return this._snapshotVersion;
        await this.database.withClient(async (client) => {
            const result = await client.query(
                'SELECT snapshot_version FROM trusted_rustdesk_controllers_watermark WHERE singleton = TRUE',
            );
            this._snapshotVersion = Number(result.rows[0]?.snapshot_version || 0);
        });
        this._loaded = true;
        return this._snapshotVersion;
    }

    currentSnapshotVersion() {
        return this._snapshotVersion;
    }

    async listAll() {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT id, controller_id, controller_hwid_hash, label, scope, enabled,
                       created_at, expires_at, audit_note
                FROM trusted_rustdesk_controllers
                ORDER BY scope, controller_id, COALESCE(controller_hwid_hash, '')
            `);
            return result.rows.map(mapRow);
        });
    }

    async listForMachine(machineId) {
        const sanitizedId = String(machineId || '').trim();
        if (!sanitizedId) return [];

        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT controller_id, controller_hwid_hash, label, scope, enabled,
                       expires_at
                FROM trusted_rustdesk_controllers
                WHERE enabled = TRUE
                  AND (expires_at IS NULL OR expires_at > NOW())
                  AND (scope = 'global' OR scope = 'machine:' || $1)
                ORDER BY controller_id
            `, [sanitizedId]);
            return result.rows.map((row) => ({
                controllerId: row.controller_id,
                controllerHwidHash: row.controller_hwid_hash || null,
                scope: row.scope,
                enabled: !!row.enabled,
                expiresAt: row.expires_at ? new Date(row.expires_at).getTime() : null,
                label: row.label || undefined,
            }));
        });
    }

    async upsert(record) {
        await this.ensureLoaded();
        const normalized = normalizeRecord(record);

        return this.database.withTransaction(async (client) => {
            const result = await client.query(`
                INSERT INTO trusted_rustdesk_controllers
                    (controller_id, controller_hwid_hash, label, scope, enabled, expires_at, audit_note)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (controller_id, COALESCE(controller_hwid_hash, ''), scope)
                DO UPDATE SET
                    label = EXCLUDED.label,
                    enabled = EXCLUDED.enabled,
                    expires_at = EXCLUDED.expires_at,
                    audit_note = EXCLUDED.audit_note
                RETURNING id, controller_id, controller_hwid_hash, label, scope, enabled,
                          created_at, expires_at, audit_note
            `, [
                normalized.controllerId,
                normalized.controllerHwidHash,
                normalized.label,
                normalized.scope,
                normalized.enabled,
                normalized.expiresAt,
                normalized.auditNote,
            ]);

            await this._bumpWatermark(client);
            return mapRow(result.rows[0]);
        });
    }

    async delete(id) {
        await this.ensureLoaded();
        return this.database.withTransaction(async (client) => {
            const result = await client.query(
                'DELETE FROM trusted_rustdesk_controllers WHERE id = $1 RETURNING id',
                [String(id || '').trim()],
            );
            if (result.rowCount === 0) {
                return false;
            }

            await this._bumpWatermark(client);
            return true;
        });
    }

    async _bumpWatermark(client) {
        const result = await client.query(`
            UPDATE trusted_rustdesk_controllers_watermark
            SET snapshot_version = snapshot_version + 1,
                updated_at = NOW()
            WHERE singleton = TRUE
            RETURNING snapshot_version
        `);
        this._snapshotVersion = Number(result.rows[0]?.snapshot_version || 0);
        return this._snapshotVersion;
    }
}

function normalizeRecord(input) {
    if (!input || typeof input !== 'object') {
        throw new Error('controller record 不能为空');
    }

    const controllerId = String(input.controllerId || input.controller_id || '').trim();
    if (!controllerId) {
        throw new Error('controllerId 不能为空');
    }

    const scope = String(input.scope || 'global').trim();
    if (scope !== 'global' && !scope.startsWith('machine:')) {
        throw new Error('scope 必须为 "global" 或 "machine:<machineId>"');
    }

    const hwidRaw = input.controllerHwidHash || input.controller_hwid_hash;
    let controllerHwidHash = null;
    if (hwidRaw !== null && hwidRaw !== undefined && String(hwidRaw).trim() !== '') {
        const cleaned = String(hwidRaw).trim().toLowerCase();
        if (!/^[0-9a-f]{64}$/.test(cleaned)) {
            throw new Error('controllerHwidHash 必须是 64 位小写十六进制 SHA-256');
        }
        controllerHwidHash = cleaned;
    }

    const expiresAtRaw = input.expiresAt ?? input.expires_at;
    let expiresAt = null;
    if (expiresAtRaw !== null && expiresAtRaw !== undefined && expiresAtRaw !== '') {
        const date = new Date(typeof expiresAtRaw === 'number' ? expiresAtRaw : String(expiresAtRaw));
        if (Number.isNaN(date.getTime())) {
            throw new Error('expiresAt 不是合法时间');
        }
        expiresAt = date.toISOString();
    }

    return {
        controllerId,
        controllerHwidHash,
        label: String(input.label || '').trim() || null,
        scope,
        enabled: input.enabled === undefined ? true : !!input.enabled,
        expiresAt,
        auditNote: String(input.auditNote || input.audit_note || '').trim() || null,
    };
}

function mapRow(row) {
    if (!row) return null;
    return {
        id: row.id,
        controllerId: row.controller_id,
        controllerHwidHash: row.controller_hwid_hash || null,
        label: row.label || null,
        scope: row.scope,
        enabled: !!row.enabled,
        createdAt: row.created_at instanceof Date ? row.created_at.toISOString() : row.created_at,
        expiresAt: row.expires_at instanceof Date ? row.expires_at.toISOString() : row.expires_at,
        auditNote: row.audit_note || null,
    };
}

module.exports = {
    TrustedControllerStore,
    normalizeRecord,
    mapRow,
};
