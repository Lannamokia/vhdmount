const { Pool } = require('pg');

function normalizeDbConfig(rawConfig = {}) {
    const config = {
        host: String(rawConfig.host || '').trim(),
        port: Number(rawConfig.port || 5432),
        database: String(rawConfig.database || '').trim(),
        user: String(rawConfig.user || '').trim(),
        password: String(rawConfig.password || '').trim(),
        ssl: Boolean(rawConfig.ssl),
        max: Number(rawConfig.max || 20),
        idleTimeoutMillis: Number(rawConfig.idleTimeoutMillis || 30000),
        connectionTimeoutMillis: Number(rawConfig.connectionTimeoutMillis || 5000),
    };

    if (!config.host || !config.database || !config.user || !config.password) {
        throw new Error('数据库配置不完整，必须提供 host、database、user、password');
    }

    if (!Number.isFinite(config.port) || config.port <= 0) {
        throw new Error('数据库端口无效');
    }

    return {
        host: config.host,
        port: config.port,
        database: config.database,
        user: config.user,
        password: config.password,
        max: config.max,
        idleTimeoutMillis: config.idleTimeoutMillis,
        connectionTimeoutMillis: config.connectionTimeoutMillis,
        ssl: config.ssl ? { rejectUnauthorized: true } : false,
    };
}

class PostgresDatabase {
    constructor(rawConfig, logger = console) {
        this.logger = logger;
        this.config = normalizeDbConfig(rawConfig);
        this.pool = new Pool(this.config);
        this.isConnected = false;
    }

    async initialize() {
        const client = await this.pool.connect();
        try {
            await client.query('SELECT NOW()');
            await client.query(`
                CREATE TABLE IF NOT EXISTS machines (
                    id SERIAL PRIMARY KEY,
                    machine_id VARCHAR(64) UNIQUE NOT NULL,
                    protected BOOLEAN DEFAULT FALSE,
                    vhd_keyword VARCHAR(64) DEFAULT 'SDEZ',
                    evhd_password TEXT DEFAULT NULL,
                    key_id VARCHAR(128) DEFAULT NULL,
                    key_type VARCHAR(32) DEFAULT NULL,
                    pubkey_pem TEXT DEFAULT NULL,
                    approved BOOLEAN DEFAULT FALSE,
                    approved_at TIMESTAMP DEFAULT NULL,
                    revoked BOOLEAN DEFAULT FALSE,
                    revoked_at TIMESTAMP DEFAULT NULL,
                    last_seen TIMESTAMP DEFAULT NULL,
                    registration_cert_fingerprint VARCHAR(128) DEFAULT NULL,
                    registration_cert_subject TEXT DEFAULT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            `);

            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS evhd_password TEXT');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_id VARCHAR(128)');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_type VARCHAR(32)');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS pubkey_pem TEXT');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved BOOLEAN DEFAULT FALSE');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved_at TIMESTAMP');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked BOOLEAN DEFAULT FALSE');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMP');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_fingerprint VARCHAR(128)');
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_subject TEXT');

            await client.query(`
                CREATE OR REPLACE FUNCTION update_updated_at_column()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$ language 'plpgsql'
            `);
            await client.query('DROP TRIGGER IF EXISTS update_machines_updated_at ON machines');
            await client.query(`
                CREATE TRIGGER update_machines_updated_at
                BEFORE UPDATE ON machines
                FOR EACH ROW
                EXECUTE FUNCTION update_updated_at_column()
            `);

            await client.query('CREATE INDEX IF NOT EXISTS idx_machines_machine_id ON machines(machine_id)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machines_protected ON machines(protected)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machines_last_seen ON machines(last_seen)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machines_cert_fingerprint ON machines(registration_cert_fingerprint)');

            this.isConnected = true;
        } finally {
            client.release();
        }
    }

    async withClient(work) {
        const client = await this.pool.connect();
        try {
            return await work(client);
        } finally {
            client.release();
        }
    }

    async getMachine(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    SELECT id, machine_id, protected, vhd_keyword,
                           (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                           key_id, key_type, pubkey_pem,
                           approved, approved_at, revoked, revoked_at, last_seen,
                           registration_cert_fingerprint, registration_cert_subject,
                           created_at, updated_at
                    FROM machines
                    WHERE machine_id = $1
                `, [machineId]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('获取机台信息失败:', error.message);
            return null;
        }
    }

    async upsertMachine(machineId, isProtected = false, vhdKeyword = 'SDEZ') {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    INSERT INTO machines (machine_id, protected, vhd_keyword)
                    VALUES ($1, $2, $3)
                    ON CONFLICT (machine_id)
                    DO UPDATE SET
                        protected = EXCLUDED.protected,
                        vhd_keyword = EXCLUDED.vhd_keyword,
                        updated_at = CURRENT_TIMESTAMP
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, isProtected, vhdKeyword]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('更新机台信息失败:', error.message);
            return null;
        }
    }

    async updateMachineProtection(machineId, isProtected) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET protected = $2, updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, isProtected]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('更新机台保护状态失败:', error.message);
            return null;
        }
    }

    async updateMachineVhdKeyword(machineId, vhdKeyword) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET vhd_keyword = $2, updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, vhdKeyword]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('更新机台VHD关键词失败:', error.message);
            return null;
        }
    }

    async getMachineEvhdPassword(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query('SELECT evhd_password FROM machines WHERE machine_id = $1', [machineId]);
                return result.rows[0]?.evhd_password || null;
            });
        } catch (error) {
            this.logger.error('获取机台EVHD密码失败:', error.message);
            return null;
        }
    }

    async updateMachineEvhdPassword(machineId, evhdPassword) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET evhd_password = $2, updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, evhdPassword]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('更新机台EVHD密码失败:', error.message);
            return null;
        }
    }

    async getAllMachines() {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    SELECT machine_id, protected, vhd_keyword,
                           (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                           key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                           registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                    FROM machines
                    ORDER BY machine_id
                `);
                return result.rows;
            });
        } catch (error) {
            this.logger.error('获取所有机台信息失败:', error.message);
            return [];
        }
    }

    async deleteMachine(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query('DELETE FROM machines WHERE machine_id = $1 RETURNING machine_id', [machineId]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('删除机台失败:', error.message);
            return null;
        }
    }

    async updateMachineLastSeen(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET last_seen = CURRENT_TIMESTAMP,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING last_seen
                `, [machineId]);
                return result.rows[0]?.last_seen || null;
            });
        } catch (error) {
            this.logger.error('更新机台最近在线时间失败:', error.message);
            return null;
        }
    }

    async updateMachineKey(machineId, { keyId, keyType, pubkeyPem, registrationCertFingerprint, registrationCertSubject }) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    INSERT INTO machines (
                        machine_id,
                        key_id,
                        key_type,
                        pubkey_pem,
                        approved,
                        revoked,
                        registration_cert_fingerprint,
                        registration_cert_subject,
                        updated_at
                    )
                    VALUES ($1, $2, $3, $4, FALSE, FALSE, $5, $6, CURRENT_TIMESTAMP)
                    ON CONFLICT (machine_id)
                    DO UPDATE SET
                        key_id = EXCLUDED.key_id,
                        key_type = EXCLUDED.key_type,
                        pubkey_pem = EXCLUDED.pubkey_pem,
                        approved = FALSE,
                        approved_at = NULL,
                        revoked = FALSE,
                        revoked_at = NULL,
                        registration_cert_fingerprint = EXCLUDED.registration_cert_fingerprint,
                        registration_cert_subject = EXCLUDED.registration_cert_subject,
                        updated_at = CURRENT_TIMESTAMP
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, keyId, keyType, pubkeyPem, registrationCertFingerprint || null, registrationCertSubject || null]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('更新机台密钥失败:', error.message);
            return null;
        }
    }

    async approveMachine(machineId, approved) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET approved = $2,
                        approved_at = CASE WHEN $2 THEN CURRENT_TIMESTAMP ELSE NULL END,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId, !!approved]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('审批机台失败:', error.message);
            return null;
        }
    }

    async revokeMachineKey(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET key_id = NULL,
                        key_type = NULL,
                        pubkey_pem = NULL,
                        approved = FALSE,
                        approved_at = NULL,
                        revoked = FALSE,
                        revoked_at = NULL,
                        registration_cert_fingerprint = NULL,
                        registration_cert_subject = NULL,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING id, machine_id, protected, vhd_keyword,
                              (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
                              key_id, key_type, approved, approved_at, revoked, revoked_at, last_seen,
                              registration_cert_fingerprint, registration_cert_subject, created_at, updated_at
                `, [machineId]);
                return result.rows[0] || null;
            });
        } catch (error) {
            this.logger.error('重置机台注册状态失败:', error.message);
            return null;
        }
    }

    async close() {
        await this.pool.end();
    }
}

function createDatabase(rawConfig, logger = console) {
    return new PostgresDatabase(rawConfig, logger);
}

module.exports = {
    createDatabase,
    normalizeDbConfig,
    PostgresDatabase,
};