ALTER TABLE machines ADD COLUMN IF NOT EXISTS log_retention_active_days_override INTEGER;

CREATE TABLE IF NOT EXISTS service_runtime_settings (
    setting_key VARCHAR(128) NOT NULL,
    setting_value_json JSONB NOT NULL,
    updated_by VARCHAR(128) DEFAULT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE service_runtime_settings ADD COLUMN IF NOT EXISTS setting_key VARCHAR(128);
ALTER TABLE service_runtime_settings ADD COLUMN IF NOT EXISTS setting_value_json JSONB;
ALTER TABLE service_runtime_settings ADD COLUMN IF NOT EXISTS updated_by VARCHAR(128);
ALTER TABLE service_runtime_settings ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;

CREATE UNIQUE INDEX IF NOT EXISTS idx_service_runtime_settings_key ON service_runtime_settings(setting_key);

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
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS machine_id VARCHAR(64);
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS session_id VARCHAR(128);
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS app_version VARCHAR(64) DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS os_version VARCHAR(256) DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS started_at TIMESTAMP DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS last_upload_at TIMESTAMP DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS last_event_at TIMESTAMP DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS total_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS warn_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS error_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS last_level VARCHAR(16) DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS last_component VARCHAR(128) DEFAULT NULL;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE machine_log_sessions ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;

DROP TRIGGER IF EXISTS update_machine_log_sessions_updated_at ON machine_log_sessions;
CREATE TRIGGER update_machine_log_sessions_updated_at
    BEFORE UPDATE ON machine_log_sessions
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE UNIQUE INDEX IF NOT EXISTS idx_machine_log_sessions_machine_session ON machine_log_sessions(machine_id, session_id);
CREATE INDEX IF NOT EXISTS idx_machine_log_sessions_machine_last_event ON machine_log_sessions(machine_id, last_event_at DESC);

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
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS machine_id VARCHAR(64);
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS session_id VARCHAR(128);
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS seq BIGINT;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS occurred_at TIMESTAMP;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS log_day DATE;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS received_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS level VARCHAR(16);
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS component VARCHAR(128);
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS event_key VARCHAR(128);
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS message TEXT;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS raw_text TEXT;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS upload_request_id VARCHAR(128) DEFAULT NULL;
ALTER TABLE machine_log_entries ADD COLUMN IF NOT EXISTS created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP;

CREATE UNIQUE INDEX IF NOT EXISTS idx_machine_log_entries_machine_session_seq ON machine_log_entries(machine_id, session_id, seq);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_occurred ON machine_log_entries(machine_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_machine_day ON machine_log_entries(machine_id, log_day DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_session_seq ON machine_log_entries(session_id, seq);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_level_occurred ON machine_log_entries(level, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_component_occurred ON machine_log_entries(component, occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_machine_log_entries_event_occurred ON machine_log_entries(event_key, occurred_at DESC);