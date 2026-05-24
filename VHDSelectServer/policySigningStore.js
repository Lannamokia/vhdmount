'use strict';

const crypto = require('crypto');

/**
 * Bridge_Policy_Signing_Pubkey 私钥管理（决策点 7 / Requirement 15.10）。
 *
 * 与 machine_keys / registration_certificates / Custom_Server_Injection ed25519 license
 * 私钥**完全独立**，不复用任何既有密钥。RSA-3072 PKCS1-SHA256。
 *
 * 服务端首次启动时由 ensureBridgePolicyKey() 钩子检测无 active 行 → 生成 RSA-3072 →
 * INSERT activated_at = NOW()。
 */
class PolicySigningStore {
    constructor(database, logger = console) {
        this.database = database;
        this.logger = logger;
    }

    async ensureBridgePolicyKey() {
        return this.database.withTransaction(async (client) => {
            const existing = await client.query(`
                SELECT key_id FROM bridge_policy_signing_keys
                WHERE activated_at IS NOT NULL
                LIMIT 1
            `);
            if (existing.rows.length > 0) {
                return { keyId: existing.rows[0].key_id, generated: false };
            }

            const { keyId, publicKeyPem, privateKeyPem } = generateBridgePolicyKeyPair();
            await client.query(`
                INSERT INTO bridge_policy_signing_keys
                    (key_id, public_key_pem, private_key_pem, activated_at)
                VALUES ($1, $2, $3, NOW())
            `, [keyId, publicKeyPem, privateKeyPem]);

            this.logger.info(`已生成 Bridge_Policy_Signing_Pubkey 初始密钥对 ${keyId}`);
            return { keyId, generated: true };
        });
    }

    async getActiveSigningKey() {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT key_id, public_key_pem, private_key_pem
                FROM bridge_policy_signing_keys
                WHERE activated_at IS NOT NULL
                LIMIT 1
            `);
            if (result.rows.length === 0) {
                throw new Error('Bridge_Policy_Signing_Pubkey 尚未生成');
            }
            const row = result.rows[0];
            return {
                keyId: row.key_id,
                publicKeyPem: row.public_key_pem,
                privateKeyPem: row.private_key_pem,
            };
        });
    }

    async regenerate() {
        return this.database.withTransaction(async (client) => {
            const { keyId, publicKeyPem, privateKeyPem } = generateBridgePolicyKeyPair();
            await client.query(
                'UPDATE bridge_policy_signing_keys SET activated_at = NULL WHERE activated_at IS NOT NULL',
            );
            await client.query(`
                INSERT INTO bridge_policy_signing_keys
                    (key_id, public_key_pem, private_key_pem, activated_at)
                VALUES ($1, $2, $3, NOW())
            `, [keyId, publicKeyPem, privateKeyPem]);
            return { keyId, publicKeyPem };
        });
    }

    /**
     * 用当前 active 私钥对 payload 字节做 RSA-PKCS1-SHA256 签名，返回 base64。
     */
    async signPayload(payloadBytes) {
        const active = await this.getActiveSigningKey();
        const sig = crypto.sign('sha256', Buffer.isBuffer(payloadBytes) ? payloadBytes : Buffer.from(payloadBytes), {
            key: active.privateKeyPem,
            padding: crypto.constants.RSA_PKCS1_PADDING,
        });
        return {
            keyId: active.keyId,
            publicKeyPem: active.publicKeyPem,
            signatureBase64: sig.toString('base64'),
        };
    }
}

function generateBridgePolicyKeyPair() {
    const { publicKey, privateKey } = crypto.generateKeyPairSync('rsa', {
        modulusLength: 3072,
        publicKeyEncoding: { type: 'spki', format: 'pem' },
        privateKeyEncoding: { type: 'pkcs8', format: 'pem' },
    });
    const keyId = 'bridge_policy_' + crypto.randomBytes(8).toString('hex');
    return {
        keyId,
        publicKeyPem: publicKey,
        privateKeyPem: privateKey,
    };
}

module.exports = {
    PolicySigningStore,
};
