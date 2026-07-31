'use strict';

/**
 * RustDeskClientSharedSecret 服务端持久化（决策点 1）。
 *
 * 录入语义：
 *  - 取 MAX(secret_version) + 1 作为新版本号；
 *  - 把所有旧 active 行的 activated_at 置 NULL；
 *  - 插入新行并 activated_at = NOW()。
 *
 * 列级保护：当前实现把 key_material 直接以 BYTEA 存放；后续可在外层叠 pgcrypto。
 * design.md 决策点 1 已说明列级加密强度与既有部署密码同等。
 */
class BridgeSecretStore {
    constructor(database, logger = console) {
        this.database = database;
        this.logger = logger;
    }

    async listVersions() {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT secret_version, created_at, created_by_user_id, activated_at, audit_note
                FROM rustdesk_bridge_secrets
                ORDER BY secret_version DESC
            `);
            return result.rows.map((row) => ({
                secretVersion: Number(row.secret_version),
                createdAt: toIsoString(row.created_at),
                createdByUserId: row.created_by_user_id || null,
                activatedAt: toIsoString(row.activated_at),
                auditNote: row.audit_note || null,
            }));
        });
    }

    async getActive() {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT secret_version, key_material
                FROM rustdesk_bridge_secrets
                WHERE activated_at IS NOT NULL
                LIMIT 1
            `);
            if (result.rows.length === 0) return null;
            const row = result.rows[0];
            return {
                secretVersion: Number(row.secret_version),
                keyMaterial: Buffer.isBuffer(row.key_material) ? row.key_material : Buffer.from(row.key_material),
            };
        });
    }

    async insertAndActivate({ keyMaterial, createdByUserId = null, auditNote = null }) {
        if (!Buffer.isBuffer(keyMaterial) || keyMaterial.length !== 32) {
            throw new Error('keyMaterial 必须是 32 字节 Buffer');
        }

        return this.database.withTransaction(async (client) => {
            const versionResult = await client.query(
                'SELECT COALESCE(MAX(secret_version), -1) + 1 AS next_version FROM rustdesk_bridge_secrets',
            );
            const nextVersion = Number(versionResult.rows[0].next_version);
            if (nextVersion > 4294967295) {
                throw new Error('secretVersion 超过 u32 上界');
            }

            await client.query(
                'UPDATE rustdesk_bridge_secrets SET activated_at = NULL WHERE activated_at IS NOT NULL',
            );

            const result = await client.query(`
                INSERT INTO rustdesk_bridge_secrets
                    (secret_version, key_material, created_by_user_id, audit_note, activated_at)
                VALUES ($1, $2, $3, $4, NOW())
                RETURNING secret_version, created_at, activated_at
            `, [nextVersion, keyMaterial, createdByUserId, auditNote]);

            const row = result.rows[0];
            return {
                secretVersion: Number(row.secret_version),
                createdAt: toIsoString(row.created_at),
                activatedAt: toIsoString(row.activated_at),
                createdByUserId,
                auditNote,
            };
        });
    }
}

function toIsoString(value) {
    if (!value) return null;
    return value instanceof Date ? value.toISOString() : String(value);
}

module.exports = {
    BridgeSecretStore,
};
