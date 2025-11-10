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
    evhd_password TEXT DEFAULT NULL,
    -- 新增：密钥与审批相关字段
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

-- 迁移兼容：确保现版本使用的 evhd_password 列存在且为 TEXT 类型
ALTER TABLE machines ADD COLUMN IF NOT EXISTS evhd_password TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_id VARCHAR(64);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_type VARCHAR(32);
ALTER TABLE machines ADD COLUMN IF NOT EXISTS pubkey_pem TEXT;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved_at TIMESTAMP;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked BOOLEAN DEFAULT FALSE;
ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMP;
-- 新增：最近在线审计列
ALTER TABLE machines ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP;

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

-- 取消插入示例数据，保持初始为空，由运行时自动创建或通过接口写入

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
CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id);
CREATE INDEX IF NOT EXISTS idx_machines_last_seen ON machines(last_seen);
CREATE INDEX IF NOT EXISTS idx_admin_settings_key ON admin_settings(setting_key);

-- 显示表结构
\d machines;
\d admin_settings;