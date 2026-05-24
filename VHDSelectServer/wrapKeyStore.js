'use strict';

const crypto = require('crypto');

const DEFAULT_TTL_MS = 600_000; // 10 分钟（与 §15.6 / §6.7 默认一致）

/**
 * Password_Wrap_Key 持久化 + LRU-2（任一机台同时最多 2 份未过期 K）。
 *
 * issue(machineId) 步骤：
 *  (a) 随机 32 字节 K；
 *  (b) 取机台 TPM 公钥 RSA-OAEP-SHA256 加密 K → wrapKeyCipher；
 *  (c) 32-hex wrapKeyId；
 *  (d) LRU-2：DELETE 同 machine_id 中按 issued_at 升序的第 2 条之后的所有条目（保留最新 1 份再插新的 = 2 份）；
 *  (e) INSERT (wrap_key_id, machine_id, key_material, issued_at, expires_at)。
 */
class WrapKeyStore {
    constructor(database, logger = console) {
        this.database = database;
        this.logger = logger;
    }

    async issue(machineId, machinePubkeyPem, { ttlMs = DEFAULT_TTL_MS } = {}) {
        if (!machineId) throw new Error('machineId 不能为空');
        if (!machinePubkeyPem) throw new Error('machinePubkeyPem 不能为空');

        const keyMaterial = crypto.randomBytes(32);
        const wrapKeyId = crypto.randomBytes(16).toString('hex');
        const wrapKeyCipher = crypto.publicEncrypt({
            key: machinePubkeyPem,
            padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
            oaepHash: 'sha256',
        }, keyMaterial);

        return this.database.withTransaction(async (client) => {
            // LRU-2：删除 machineId 下按 issued_at 升序第 2 条之后所有条目
            await client.query(`
                DELETE FROM rustdesk_wrap_keys
                WHERE wrap_key_id IN (
                    SELECT wrap_key_id FROM rustdesk_wrap_keys
                    WHERE machine_id = $1
                    ORDER BY issued_at ASC
                    OFFSET 1
                )
            `, [machineId]);

            const issuedAt = new Date();
            const expiresAt = new Date(issuedAt.getTime() + ttlMs);
            await client.query(`
                INSERT INTO rustdesk_wrap_keys
                    (wrap_key_id, machine_id, key_material, issued_at, expires_at)
                VALUES ($1, $2, $3, $4, $5)
            `, [wrapKeyId, machineId, keyMaterial, issuedAt.toISOString(), expiresAt.toISOString()]);

            return {
                wrapKeyId,
                wrapKeyCipher,
                keyMaterial,
                issuedAtMs: issuedAt.getTime(),
                ttlMs,
                expiresAtMs: expiresAt.getTime(),
            };
        });
    }

    async getKeyMaterial(wrapKeyId, machineId) {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT key_material, expires_at
                FROM rustdesk_wrap_keys
                WHERE wrap_key_id = $1 AND machine_id = $2
            `, [wrapKeyId, machineId]);
            if (result.rows.length === 0) return null;

            const row = result.rows[0];
            const expiresAt = row.expires_at instanceof Date ? row.expires_at : new Date(row.expires_at);
            if (Number.isFinite(expiresAt.getTime()) && expiresAt.getTime() <= Date.now()) {
                return { expired: true };
            }
            return {
                keyMaterial: Buffer.isBuffer(row.key_material) ? row.key_material : Buffer.from(row.key_material),
                expiresAtMs: expiresAt.getTime(),
            };
        });
    }

    async deleteExpired() {
        return this.database.withClient(async (client) => {
            const result = await client.query(
                'DELETE FROM rustdesk_wrap_keys WHERE expires_at <= NOW() RETURNING wrap_key_id',
            );
            return result.rowCount;
        });
    }
}

module.exports = {
    WrapKeyStore,
    DEFAULT_TTL_MS,
};
