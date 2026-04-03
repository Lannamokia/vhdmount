-- PostgreSQL 数据库配置脚本
-- 请使用具有建库权限的账号执行。
-- 管理员密码、TOTP 秘钥、Session Secret 与可信注册证书不在数据库中落盘，
-- 而是由服务端初始化向导写入 server-security.json。

CREATE DATABASE vhd_select
    WITH
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'Chinese (Simplified)_China.936'
    LC_CTYPE = 'Chinese (Simplified)_China.936'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

\c vhd_select;

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

SELECT 'Database setup completed successfully!' AS status;