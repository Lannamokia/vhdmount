-- VHD Select Server 数据库初始化脚本

-- 创建数据库（如果不存在）
-- CREATE DATABASE vhd_select;

-- 使用数据库
-- \c vhd_select;

-- 创建机台表
CREATE TABLE IF NOT EXISTS machines (
    id SERIAL PRIMARY KEY,
    machine_id VARCHAR(255) UNIQUE NOT NULL,
    protected BOOLEAN DEFAULT FALSE,
    vhd_keyword VARCHAR(255) DEFAULT 'SDEZ',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 创建更新时间触发器函数
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- 为machines表创建更新时间触发器
DROP TRIGGER IF EXISTS update_machines_updated_at ON machines;
CREATE TRIGGER update_machines_updated_at
    BEFORE UPDATE ON machines
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- 插入一些示例数据
INSERT INTO machines (machine_id, protected, vhd_keyword) 
VALUES 
    ('MACHINE001', FALSE, 'SDEZ'),
    ('MACHINE002', FALSE, 'PROD'),
    ('MACHINE003', TRUE, 'TEST')
ON CONFLICT (machine_id) DO NOTHING;

-- 创建管理员密码表
CREATE TABLE IF NOT EXISTS admin_settings (
    id SERIAL PRIMARY KEY,
    setting_key VARCHAR(255) UNIQUE NOT NULL,
    setting_value TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 为admin_settings表创建更新时间触发器
DROP TRIGGER IF EXISTS update_admin_settings_updated_at ON admin_settings;
CREATE TRIGGER update_admin_settings_updated_at
    BEFORE UPDATE ON admin_settings
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- 插入默认管理员密码 (admin123的bcrypt哈希)
INSERT INTO admin_settings (setting_key, setting_value) 
VALUES ('admin_password_hash', '$2a$10$uTXfi1JIapQn08aVZL1mKO9B6jU8GgOmW6bIuMP.pTk/M.pxf86Su')
ON CONFLICT (setting_key) DO NOTHING;

-- 创建索引以提高查询性能
CREATE INDEX IF NOT EXISTS idx_machines_machine_id ON machines(machine_id);
CREATE INDEX IF NOT EXISTS idx_machines_protected ON machines(protected);
CREATE INDEX IF NOT EXISTS idx_admin_settings_key ON admin_settings(setting_key);

-- 显示表结构
\d machines;
\d admin_settings;