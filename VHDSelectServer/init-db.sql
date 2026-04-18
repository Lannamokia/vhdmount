-- VHD Select Server 数据库初始化脚本
-- 安全初始化（管理员密码、Session Secret、TOTP、可信注册证书）由服务端首次启动流程生成，
-- 本脚本仅负责创建业务表结构。

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
);

ALTER TABLE machines ADD COLUMN IF NOT EXISTS evhd_password TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_id VARCHAR(128);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_type VARCHAR(32);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS pubkey_pem TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved_at TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_fingerprint VARCHAR(128);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS registration_cert_subject TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS log_retention_active_days_override INTEGER;

CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

DROP TRIGGER IF EXISTS update_machines_updated_at ON machines;
CREATE TRIGGER update_machines_updated_at
    BEFORE UPDATE ON machines
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE INDEX IF NOT EXISTS idx_machines_machine_id ON machines(machine_id);
CREATE INDEX IF NOT EXISTS idx_machines_protected ON machines(protected);
CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id);
CREATE INDEX IF NOT EXISTS idx_machines_last_seen ON machines(last_seen);
CREATE INDEX IF NOT EXISTS idx_machines_cert_fingerprint ON machines(registration_cert_fingerprint);

CREATE TABLE IF NOT EXISTS service_runtime_settings (
    setting_key VARCHAR(128) PRIMARY KEY,
    setting_value_json JSONB NOT NULL,
    updated_by VARCHAR(128) DEFAULT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

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
);

DROP TRIGGER IF EXISTS update_machine_log_sessions_updated_at ON machine_log_sessions;
CREATE TRIGGER update_machine_log_sessions_updated_at
    BEFORE UPDATE ON machine_log_sessions
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

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
);

CREATE INDEX IF NOT EXISTS idx_machine_log_sessions_machine_last_event
    ON machine_log_sessions(machine_id, last_event_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_occurred
    ON machine_log_entries(machine_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_day
    ON machine_log_entries(machine_id, log_day DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_session_seq
    ON machine_log_entries(session_id, seq);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_level_occurred
    ON machine_log_entries(level, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_component_occurred
    ON machine_log_entries(component, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_event_occurred
    ON machine_log_entries(event_key, occurred_at DESC);