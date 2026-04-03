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