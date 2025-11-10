-- PostgreSQL数据库配置脚本
-- 请在安装PostgreSQL后，使用管理员权限运行此脚本

-- 1. 创建数据库
CREATE DATABASE vhd_select
    WITH 
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'Chinese (Simplified)_China.936'
    LC_CTYPE = 'Chinese (Simplified)_China.936'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

-- 2. 连接到新创建的数据库
\c vhd_select;

-- 3. 创建machines表
CREATE TABLE IF NOT EXISTS machines (
    id SERIAL PRIMARY KEY,
    machine_id VARCHAR(255) UNIQUE NOT NULL,
    protected BOOLEAN DEFAULT FALSE,
    vhd_keyword VARCHAR(255) DEFAULT 'SDEZ',
    evhd_password TEXT DEFAULT NULL,
    -- 密钥与审批相关字段
    key_id VARCHAR(64) DEFAULT NULL,
    key_type VARCHAR(32) DEFAULT NULL,
    pubkey_pem TEXT DEFAULT NULL,
    approved BOOLEAN DEFAULT FALSE,
    approved_at TIMESTAMP DEFAULT NULL,
    revoked BOOLEAN DEFAULT FALSE,
    revoked_at TIMESTAMP DEFAULT NULL,
    -- 最近在线审计
    last_seen TIMESTAMP DEFAULT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 4. 创建更新时间戳的触发器函数
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- 5. 创建触发器
CREATE TRIGGER update_machines_updated_at 
    BEFORE UPDATE ON machines 
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

-- 6. 创建索引以提高查询性能
CREATE INDEX IF NOT EXISTS idx_machines_machine_id ON machines(machine_id);
CREATE INDEX IF NOT EXISTS idx_machines_protected ON machines(protected);
CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id);
CREATE INDEX IF NOT EXISTS idx_machines_last_seen ON machines(last_seen);

-- 7. 创建管理员设置表及触发器
CREATE TABLE IF NOT EXISTS admin_settings (
    id SERIAL PRIMARY KEY,
    setting_key VARCHAR(255) UNIQUE NOT NULL,
    setting_value TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

DROP TRIGGER IF EXISTS update_admin_settings_updated_at ON admin_settings;
CREATE TRIGGER update_admin_settings_updated_at
    BEFORE UPDATE ON admin_settings
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- 8. 插入默认管理员密码 (admin123 的 bcrypt 哈希，可在运行后修改)
INSERT INTO admin_settings (setting_key, setting_value)
VALUES ('admin_password_hash', '$2a$10$uTXfi1JIapQn08aVZL1mKO9B6jU8GgOmW6bIuMP.pTk/M.pxf86Su')
ON CONFLICT (setting_key) DO NOTHING;
CREATE INDEX IF NOT EXISTS idx_admin_settings_key ON admin_settings(setting_key);

-- 9. 显示创建结果
SELECT 'Database setup completed successfully!' as status;
SELECT * FROM machines;