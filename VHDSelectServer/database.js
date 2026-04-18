const { Pool } = require('pg');

const MACHINE_SELECT_COLUMNS = `
    id,
    machine_id,
    protected,
    vhd_keyword,
    (evhd_password IS NOT NULL AND evhd_password <> '') AS evhd_password_configured,
    key_id,
    key_type,
    pubkey_pem,
    approved,
    approved_at,
    revoked,
    revoked_at,
    last_seen,
    registration_cert_fingerprint,
    registration_cert_subject,
    log_retention_active_days_override,
    created_at,
    updated_at
`;

const SESSION_SELECT_COLUMNS = `
    machine_id,
    session_id,
    app_version,
    os_version,
    started_at,
    last_upload_at,
    last_event_at,
    total_count,
    warn_count,
    error_count,
    last_level,
    last_component,
    created_at,
    updated_at
`;

const ENTRY_SELECT_COLUMNS = `
    id,
    machine_id,
    session_id,
    seq,
    occurred_at,
    log_day,
    received_at,
    level,
    component,
    event_key,
    message,
    raw_text,
    metadata_json,
    upload_request_id,
    created_at
`;

const DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS = Object.freeze({
    defaultRetentionActiveDays: 7,
    dailyInspectionHour: 3,
    dailyInspectionMinute: 0,
    timezone: 'UTC',
    lastInspectionAt: null,
});

const MACHINE_LOG_RUNTIME_SETTING_KEY_MAP = Object.freeze({
    defaultRetentionActiveDays: 'machine_log.default_retention_active_days',
    dailyInspectionHour: 'machine_log.daily_inspection_hour',
    dailyInspectionMinute: 'machine_log.daily_inspection_minute',
    timezone: 'machine_log.timezone',
    lastInspectionAt: 'machine_log.last_inspection_at',
});

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

function mapMachineRow(row) {
    if (!row) {
        return null;
    }

    return {
        ...row,
        log_retention_active_days_override: row.log_retention_active_days_override == null
            ? null
            : Number(row.log_retention_active_days_override),
    };
}

function mapMachineLogSessionRow(row) {
    if (!row) {
        return null;
    }

    return {
        ...row,
        total_count: Number(row.total_count || 0),
        warn_count: Number(row.warn_count || 0),
        error_count: Number(row.error_count || 0),
    };
}

function mapMachineLogEntryRow(row) {
    if (!row) {
        return null;
    }

    return {
        ...row,
        id: Number(row.id || 0),
        seq: Number(row.seq || 0),
        metadata_json: row.metadata_json || {},
    };
}

function normalizeMachineLogRuntimeSettings(raw = {}) {
    return {
        defaultRetentionActiveDays: Number(raw.defaultRetentionActiveDays || DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS.defaultRetentionActiveDays),
        dailyInspectionHour: Number(raw.dailyInspectionHour ?? DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS.dailyInspectionHour),
        dailyInspectionMinute: Number(raw.dailyInspectionMinute ?? DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS.dailyInspectionMinute),
        timezone: String(raw.timezone || DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS.timezone).trim() || DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS.timezone,
        lastInspectionAt: raw.lastInspectionAt || null,
    };
}

function clampText(value, maxLength) {
    const text = String(value == null ? '' : value);
    return text.length > maxLength ? text.slice(0, maxLength) : text;
}

function sanitizeMetadataForStorage(value, maxDepth = 4) {
    if (maxDepth <= 0) {
        return '[Truncated]';
    }

    if (value == null) {
        return null;
    }

    if (Array.isArray(value)) {
        return value.slice(0, 64).map((item) => sanitizeMetadataForStorage(item, maxDepth - 1));
    }

    if (typeof value === 'object') {
        return Object.fromEntries(
            Object.entries(value)
                .slice(0, 64)
                .map(([key, item]) => [clampText(key, 128), sanitizeMetadataForStorage(item, maxDepth - 1)]),
        );
    }

    if (typeof value === 'string') {
        return clampText(value, 1024);
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return value;
    }

    return clampText(String(value), 1024);
}

function formatLogDay(occurredAt, timeZone = 'UTC') {
    const date = occurredAt instanceof Date ? occurredAt : new Date(occurredAt);
    if (!Number.isFinite(date.getTime())) {
        throw new Error('occurredAt 无效');
    }

    const formatter = new Intl.DateTimeFormat('en-CA', {
        timeZone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
    });

    const parts = formatter.formatToParts(date);
    const year = parts.find((part) => part.type === 'year')?.value;
    const month = parts.find((part) => part.type === 'month')?.value;
    const day = parts.find((part) => part.type === 'day')?.value;
    return `${year}-${month}-${day}`;
}

function decodeCursor(cursor) {
    if (!cursor) {
        return null;
    }

    const parsed = JSON.parse(Buffer.from(String(cursor), 'base64url').toString('utf8'));
    if (!parsed || !parsed.occurredAt || !Number.isFinite(Number(parsed.id))) {
        throw new Error('cursor 无效');
    }

    return {
        occurredAt: new Date(parsed.occurredAt).toISOString(),
        id: Number(parsed.id),
    };
}

function encodeCursor(row) {
    return Buffer.from(JSON.stringify({
        occurredAt: row.occurred_at,
        id: Number(row.id),
    }), 'utf8').toString('base64url');
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
                    log_retention_active_days_override INTEGER DEFAULT NULL,
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
            await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS log_retention_active_days_override INTEGER');

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

            await client.query(`
                CREATE TABLE IF NOT EXISTS service_runtime_settings (
                    setting_key VARCHAR(128) PRIMARY KEY,
                    setting_value_json JSONB NOT NULL,
                    updated_by VARCHAR(128) DEFAULT NULL,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            `);

            await client.query(`
                CREATE TABLE IF NOT EXISTS machine_log_sessions (
                    id SERIAL PRIMARY KEY,
                    machine_id VARCHAR(64) NOT NULL REFERENCES machines(machine_id) ON DELETE CASCADE,
                    session_id VARCHAR(128) NOT NULL,
                    app_version VARCHAR(64) DEFAULT NULL,
                    os_version VARCHAR(256) DEFAULT NULL,
                    started_at TIMESTAMP DEFAULT NULL,
                    last_upload_at TIMESTAMP DEFAULT NULL,
                    last_event_at TIMESTAMP DEFAULT NULL,
                    total_count INTEGER NOT NULL DEFAULT 0,
                    warn_count INTEGER NOT NULL DEFAULT 0,
                    error_count INTEGER NOT NULL DEFAULT 0,
                    last_level VARCHAR(16) DEFAULT NULL,
                    last_component VARCHAR(128) DEFAULT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(machine_id, session_id)
                )
            `);

            await client.query('DROP TRIGGER IF EXISTS update_machine_log_sessions_updated_at ON machine_log_sessions');
            await client.query(`
                CREATE TRIGGER update_machine_log_sessions_updated_at
                BEFORE UPDATE ON machine_log_sessions
                FOR EACH ROW
                EXECUTE FUNCTION update_updated_at_column()
            `);

            await client.query(`
                CREATE TABLE IF NOT EXISTS machine_log_entries (
                    id BIGSERIAL PRIMARY KEY,
                    machine_id VARCHAR(64) NOT NULL REFERENCES machines(machine_id) ON DELETE CASCADE,
                    session_id VARCHAR(128) NOT NULL,
                    seq BIGINT NOT NULL,
                    occurred_at TIMESTAMP NOT NULL,
                    log_day DATE NOT NULL,
                    received_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    level VARCHAR(16) NOT NULL,
                    component VARCHAR(128) NOT NULL,
                    event_key VARCHAR(128) NOT NULL,
                    message TEXT NOT NULL,
                    raw_text TEXT NOT NULL,
                    metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
                    upload_request_id VARCHAR(128) DEFAULT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(machine_id, session_id, seq)
                )
            `);

            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_sessions_machine_last_event ON machine_log_sessions(machine_id, last_event_at DESC)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_occurred ON machine_log_entries(machine_id, occurred_at DESC)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_day ON machine_log_entries(machine_id, log_day DESC)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_session_seq ON machine_log_entries(session_id, seq)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_level_occurred ON machine_log_entries(level, occurred_at DESC)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_component_occurred ON machine_log_entries(component, occurred_at DESC)');
            await client.query('CREATE INDEX IF NOT EXISTS idx_machine_log_entries_event_occurred ON machine_log_entries(event_key, occurred_at DESC)');

            await this.ensureMachineLogRuntimeSettings(undefined, 'system', client);
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

    async withTransaction(work) {
        return this.withClient(async (client) => {
            await client.query('BEGIN');
            try {
                const result = await work(client);
                await client.query('COMMIT');
                return result;
            } catch (error) {
                await client.query('ROLLBACK');
                throw error;
            }
        });
    }

    async getMachine(machineId) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    SELECT ${MACHINE_SELECT_COLUMNS}
                    FROM machines
                    WHERE machine_id = $1
                `, [machineId]);
                return mapMachineRow(result.rows[0] || null);
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
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, isProtected, vhdKeyword]);
                return mapMachineRow(result.rows[0] || null);
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
                    SET protected = $2,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, isProtected]);
                return mapMachineRow(result.rows[0] || null);
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
                    SET vhd_keyword = $2,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, vhdKeyword]);
                return mapMachineRow(result.rows[0] || null);
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
                    SET evhd_password = $2,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, evhdPassword]);
                return mapMachineRow(result.rows[0] || null);
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
                    SELECT ${MACHINE_SELECT_COLUMNS}
                    FROM machines
                    ORDER BY machine_id
                `);
                return result.rows.map(mapMachineRow);
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
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, keyId, keyType, pubkeyPem, registrationCertFingerprint || null, registrationCertSubject || null]);
                return mapMachineRow(result.rows[0] || null);
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
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, !!approved]);
                return mapMachineRow(result.rows[0] || null);
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
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId]);
                return mapMachineRow(result.rows[0] || null);
            });
        } catch (error) {
            this.logger.error('重置机台注册状态失败:', error.message);
            return null;
        }
    }

    async updateMachineLogRetentionOverride(machineId, retentionActiveDaysOverride) {
        try {
            return await this.withClient(async (client) => {
                const result = await client.query(`
                    UPDATE machines
                    SET log_retention_active_days_override = $2,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE machine_id = $1
                    RETURNING ${MACHINE_SELECT_COLUMNS}
                `, [machineId, retentionActiveDaysOverride]);
                return mapMachineRow(result.rows[0] || null);
            });
        } catch (error) {
            this.logger.error('更新机台日志保留覆盖配置失败:', error.message);
            return null;
        }
    }

    async _getMachineLogRuntimeSettingsWithClient(client) {
        const result = await client.query(`
            SELECT setting_key, setting_value_json
            FROM service_runtime_settings
            WHERE setting_key = ANY($1)
        `, [Object.values(MACHINE_LOG_RUNTIME_SETTING_KEY_MAP)]);

        const next = { ...DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS };
        for (const row of result.rows) {
            const propertyKey = Object.entries(MACHINE_LOG_RUNTIME_SETTING_KEY_MAP)
                .find(([, settingKey]) => settingKey === row.setting_key)?.[0];
            if (!propertyKey) {
                continue;
            }
            next[propertyKey] = row.setting_value_json;
        }

        return normalizeMachineLogRuntimeSettings(next);
    }

    async getMachineLogRuntimeSettings() {
        try {
            return await this.withClient((client) => this._getMachineLogRuntimeSettingsWithClient(client));
        } catch (error) {
            this.logger.error('读取机台日志运行时配置失败:', error.message);
            return { ...DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS };
        }
    }

    async ensureMachineLogRuntimeSettings(settings = undefined, updatedBy = 'system', existingClient = null) {
        const client = existingClient || await this.pool.connect();
        try {
            const normalized = normalizeMachineLogRuntimeSettings({
                ...DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS,
                ...(settings || {}),
            });

            for (const [propertyKey, settingKey] of Object.entries(MACHINE_LOG_RUNTIME_SETTING_KEY_MAP)) {
                await client.query(`
                    INSERT INTO service_runtime_settings (setting_key, setting_value_json, updated_by, updated_at)
                    VALUES ($1, $2::jsonb, $3, CURRENT_TIMESTAMP)
                    ON CONFLICT (setting_key) DO NOTHING
                `, [settingKey, JSON.stringify(normalized[propertyKey]), updatedBy]);
            }

            return await this._getMachineLogRuntimeSettingsWithClient(client);
        } finally {
            if (!existingClient) {
                client.release();
            }
        }
    }

    async updateMachineLogRuntimeSettings(settings, updatedBy = 'admin', existingClient = null) {
        const work = async (client) => {
            const current = await this._getMachineLogRuntimeSettingsWithClient(client);
            const next = normalizeMachineLogRuntimeSettings({
                ...current,
                ...(settings || {}),
            });

            for (const [propertyKey, settingKey] of Object.entries(MACHINE_LOG_RUNTIME_SETTING_KEY_MAP)) {
                if (next[propertyKey] === current[propertyKey]) {
                    continue;
                }

                await client.query(`
                    INSERT INTO service_runtime_settings (setting_key, setting_value_json, updated_by, updated_at)
                    VALUES ($1, $2::jsonb, $3, CURRENT_TIMESTAMP)
                    ON CONFLICT (setting_key)
                    DO UPDATE SET
                        setting_value_json = EXCLUDED.setting_value_json,
                        updated_by = EXCLUDED.updated_by,
                        updated_at = CURRENT_TIMESTAMP
                `, [settingKey, JSON.stringify(next[propertyKey]), updatedBy]);
            }

            return next;
        };

        try {
            if (existingClient) {
                return await work(existingClient);
            }
            return await this.withTransaction(work);
        } catch (error) {
            this.logger.error('更新机台日志运行时配置失败:', error.message);
            throw error;
        }
    }

    async getMachineLogAcknowledgedSeq(machineId, sessionId, existingClient = null) {
        const work = async (client) => {
            const result = await client.query(`
                SELECT COALESCE(MAX(seq), 0) AS acknowledged_seq
                FROM machine_log_entries
                WHERE machine_id = $1
                  AND session_id = $2
            `, [machineId, sessionId]);
            return Number(result.rows[0]?.acknowledged_seq || 0);
        };

        return existingClient ? work(existingClient) : this.withClient(work);
    }

    async persistMachineLogBatch({ machineId, sessionId, appVersion, osVersion, entries, uploadRequestId, timezone }) {
        try {
            return await this.withTransaction(async (client) => {
                const settings = await this._getMachineLogRuntimeSettingsWithClient(client);
                const effectiveTimeZone = String(timezone || settings.timezone || 'UTC').trim() || 'UTC';
                const normalizedEntries = (Array.isArray(entries) ? entries : []).map((entry) => {
                    const occurredAt = new Date(entry.occurredAt || entry.occurred_at || new Date().toISOString());
                    return {
                        seq: Number(entry.seq),
                        occurredAt,
                        logDay: formatLogDay(occurredAt, effectiveTimeZone),
                        level: clampText(String(entry.level || 'info').trim().toLowerCase(), 16) || 'info',
                        component: clampText(String(entry.component || 'Program').trim(), 128) || 'Program',
                        eventKey: clampText(String(entry.eventKey || entry.event_key || 'TRACE_LINE').trim().toUpperCase(), 128) || 'TRACE_LINE',
                        message: clampText(entry.message || entry.rawText || '', 4096),
                        rawText: clampText(entry.rawText || entry.message || '', 8192),
                        metadataJson: sanitizeMetadataForStorage(entry.metadata || entry.metadata_json || {}),
                    };
                });

                if (!normalizedEntries.length) {
                    return {
                        acknowledgedSeq: await this.getMachineLogAcknowledgedSeq(machineId, sessionId, client),
                        insertedCount: 0,
                        receivedCount: 0,
                    };
                }

                const startedAt = normalizedEntries
                    .map((entry) => entry.occurredAt.getTime())
                    .reduce((minValue, currentValue) => Math.min(minValue, currentValue), Number.POSITIVE_INFINITY);
                const lastEventAt = normalizedEntries
                    .map((entry) => entry.occurredAt.getTime())
                    .reduce((maxValue, currentValue) => Math.max(maxValue, currentValue), 0);

                await client.query(`
                    INSERT INTO machine_log_sessions (
                        machine_id,
                        session_id,
                        app_version,
                        os_version,
                        started_at,
                        last_upload_at,
                        last_event_at,
                        created_at,
                        updated_at
                    )
                    VALUES ($1, $2, $3, $4, $5, CURRENT_TIMESTAMP, $6, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                    ON CONFLICT (machine_id, session_id)
                    DO UPDATE SET
                        app_version = COALESCE(EXCLUDED.app_version, machine_log_sessions.app_version),
                        os_version = COALESCE(EXCLUDED.os_version, machine_log_sessions.os_version),
                        started_at = CASE
                            WHEN machine_log_sessions.started_at IS NULL THEN EXCLUDED.started_at
                            ELSE LEAST(machine_log_sessions.started_at, EXCLUDED.started_at)
                        END,
                        last_upload_at = CURRENT_TIMESTAMP,
                        last_event_at = CASE
                            WHEN machine_log_sessions.last_event_at IS NULL THEN EXCLUDED.last_event_at
                            ELSE GREATEST(machine_log_sessions.last_event_at, EXCLUDED.last_event_at)
                        END,
                        updated_at = CURRENT_TIMESTAMP
                `, [
                    machineId,
                    sessionId,
                    appVersion || null,
                    osVersion || null,
                    new Date(startedAt).toISOString(),
                    new Date(lastEventAt).toISOString(),
                ]);

                let insertedCount = 0;
                for (const entry of normalizedEntries) {
                    const result = await client.query(`
                        INSERT INTO machine_log_entries (
                            machine_id,
                            session_id,
                            seq,
                            occurred_at,
                            log_day,
                            received_at,
                            level,
                            component,
                            event_key,
                            message,
                            raw_text,
                            metadata_json,
                            upload_request_id,
                            created_at
                        )
                        VALUES ($1, $2, $3, $4, $5, CURRENT_TIMESTAMP, $6, $7, $8, $9, $10, $11::jsonb, $12, CURRENT_TIMESTAMP)
                        ON CONFLICT (machine_id, session_id, seq) DO NOTHING
                        RETURNING seq
                    `, [
                        machineId,
                        sessionId,
                        entry.seq,
                        entry.occurredAt.toISOString(),
                        entry.logDay,
                        entry.level,
                        entry.component,
                        entry.eventKey,
                        entry.message,
                        entry.rawText,
                        JSON.stringify(entry.metadataJson || {}),
                        uploadRequestId || null,
                    ]);
                    if (result.rowCount > 0) {
                        insertedCount += 1;
                    }
                }

                await client.query(`
                    UPDATE machine_log_sessions AS session
                    SET last_upload_at = CURRENT_TIMESTAMP,
                        started_at = summary.started_at,
                        last_event_at = summary.last_event_at,
                        total_count = summary.total_count,
                        warn_count = summary.warn_count,
                        error_count = summary.error_count,
                        last_level = summary.last_level,
                        last_component = summary.last_component,
                        updated_at = CURRENT_TIMESTAMP
                    FROM (
                        SELECT machine_id,
                               session_id,
                               MIN(occurred_at) AS started_at,
                               MAX(occurred_at) AS last_event_at,
                               COUNT(*)::int AS total_count,
                               COUNT(*) FILTER (WHERE level = 'warn')::int AS warn_count,
                               COUNT(*) FILTER (WHERE level = 'error')::int AS error_count,
                               (ARRAY_AGG(level ORDER BY occurred_at DESC, id DESC))[1] AS last_level,
                               (ARRAY_AGG(component ORDER BY occurred_at DESC, id DESC))[1] AS last_component
                        FROM machine_log_entries
                        WHERE machine_id = $1
                          AND session_id = $2
                        GROUP BY machine_id, session_id
                    ) AS summary
                    WHERE session.machine_id = summary.machine_id
                      AND session.session_id = summary.session_id
                `, [machineId, sessionId]);

                return {
                    acknowledgedSeq: await this.getMachineLogAcknowledgedSeq(machineId, sessionId, client),
                    insertedCount,
                    receivedCount: normalizedEntries.length,
                };
            });
        } catch (error) {
            this.logger.error('写入机台日志批次失败:', error.message);
            throw error;
        }
    }

    async getMachineLogSessions({ machineId, from, to, limit = 50 }) {
        try {
            return await this.withClient(async (client) => {
                const conditions = [];
                const values = [];

                if (machineId) {
                    values.push(machineId);
                    conditions.push(`machine_id = $${values.length}`);
                }
                if (from) {
                    values.push(from);
                    conditions.push(`COALESCE(last_event_at, started_at, created_at) >= $${values.length}`);
                }
                if (to) {
                    values.push(to);
                    conditions.push(`COALESCE(last_event_at, started_at, created_at) <= $${values.length}`);
                }

                values.push(limit);
                const result = await client.query(`
                    SELECT ${SESSION_SELECT_COLUMNS}
                    FROM machine_log_sessions
                    ${conditions.length ? `WHERE ${conditions.join(' AND ')}` : ''}
                    ORDER BY COALESCE(last_event_at, started_at, created_at) DESC, session_id DESC
                    LIMIT $${values.length}
                `, values);
                return result.rows.map(mapMachineLogSessionRow);
            });
        } catch (error) {
            this.logger.error('读取机台日志会话失败:', error.message);
            return [];
        }
    }

    async getMachineLogs({ machineId, sessionId, level, component, eventKey, query, from, to, cursor, limit = 100 }) {
        try {
            return await this.withClient(async (client) => {
                const conditions = [];
                const values = [];

                if (machineId) {
                    values.push(machineId);
                    conditions.push(`machine_id = $${values.length}`);
                }
                if (sessionId) {
                    values.push(sessionId);
                    conditions.push(`session_id = $${values.length}`);
                }
                if (level) {
                    values.push(level);
                    conditions.push(`level = $${values.length}`);
                }
                if (component) {
                    values.push(component);
                    conditions.push(`component = $${values.length}`);
                }
                if (eventKey) {
                    values.push(eventKey);
                    conditions.push(`event_key = $${values.length}`);
                }
                if (from) {
                    values.push(from);
                    conditions.push(`occurred_at >= $${values.length}`);
                }
                if (to) {
                    values.push(to);
                    conditions.push(`occurred_at <= $${values.length}`);
                }
                if (query) {
                    values.push(`%${query}%`);
                    conditions.push(`(
                        message ILIKE $${values.length}
                        OR raw_text ILIKE $${values.length}
                        OR CAST(metadata_json AS TEXT) ILIKE $${values.length}
                    )`);
                }

                const decodedCursor = cursor ? decodeCursor(cursor) : null;
                if (decodedCursor) {
                    values.push(decodedCursor.occurredAt, decodedCursor.id);
                    conditions.push(`(occurred_at, id) < ($${values.length - 1}, $${values.length})`);
                }

                values.push(limit + 1);
                const result = await client.query(`
                    SELECT ${ENTRY_SELECT_COLUMNS}
                    FROM machine_log_entries
                    ${conditions.length ? `WHERE ${conditions.join(' AND ')}` : ''}
                    ORDER BY occurred_at DESC, id DESC
                    LIMIT $${values.length}
                `, values);

                const hasMore = result.rows.length > limit;
                const rows = hasMore ? result.rows.slice(0, limit) : result.rows;
                return {
                    entries: rows.map(mapMachineLogEntryRow),
                    nextCursor: hasMore && rows.length > 0 ? encodeCursor(rows[rows.length - 1]) : null,
                    hasMore,
                };
            });
        } catch (error) {
            this.logger.error('读取机台日志明细失败:', error.message);
            return {
                entries: [],
                nextCursor: null,
                hasMore: false,
            };
        }
    }

    async exportMachineLogs({ machineId, sessionId, level, component, eventKey, query, from, to, limit = 5000 }) {
        return this.getMachineLogs({
            machineId,
            sessionId,
            level,
            component,
            eventKey,
            query,
            from,
            to,
            limit,
        });
    }

    async runMachineLogRetentionInspection(updatedBy = 'system') {
        try {
            return await this.withTransaction(async (client) => {
                const settings = await this._getMachineLogRuntimeSettingsWithClient(client);
                const machineRows = await client.query(`
                    SELECT machine_id, log_retention_active_days_override
                    FROM machines
                    WHERE machine_id IN (
                        SELECT DISTINCT machine_id
                        FROM machine_log_entries
                    )
                    ORDER BY machine_id
                `);

                let deletedEntryCount = 0;
                let deletedSessionCount = 0;

                for (const row of machineRows.rows) {
                    const retentionActiveDays = Number(row.log_retention_active_days_override || settings.defaultRetentionActiveDays);
                    if (!Number.isFinite(retentionActiveDays) || retentionActiveDays <= 0) {
                        continue;
                    }

                    const rankedDays = await client.query(`
                        SELECT log_day
                        FROM machine_log_entries
                        WHERE machine_id = $1
                        GROUP BY log_day
                        ORDER BY log_day DESC
                    `, [row.machine_id]);

                    if (rankedDays.rows.length <= retentionActiveDays) {
                        continue;
                    }

                    const cutoffDay = rankedDays.rows[retentionActiveDays - 1]?.log_day;
                    if (!cutoffDay) {
                        continue;
                    }

                    const deleteEntriesResult = await client.query(`
                        DELETE FROM machine_log_entries
                        WHERE machine_id = $1
                          AND log_day < $2
                    `, [row.machine_id, cutoffDay]);
                    deletedEntryCount += deleteEntriesResult.rowCount || 0;

                    const deleteSessionsResult = await client.query(`
                        DELETE FROM machine_log_sessions AS session
                        WHERE session.machine_id = $1
                          AND NOT EXISTS (
                              SELECT 1
                              FROM machine_log_entries AS entry
                              WHERE entry.machine_id = session.machine_id
                                AND entry.session_id = session.session_id
                          )
                    `, [row.machine_id]);
                    deletedSessionCount += deleteSessionsResult.rowCount || 0;
                }

                const ranAt = new Date().toISOString();
                await this.updateMachineLogRuntimeSettings({ lastInspectionAt: ranAt }, updatedBy, client);

                return {
                    inspectedMachineCount: machineRows.rows.length,
                    deletedEntryCount,
                    deletedSessionCount,
                    ranAt,
                };
            });
        } catch (error) {
            this.logger.error('执行机台日志保留巡检失败:', error.message);
            throw error;
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
    DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS,
    normalizeDbConfig,
    PostgresDatabase,
};