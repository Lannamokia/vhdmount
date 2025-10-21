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

-- 创建索引以提高查询性能
CREATE INDEX IF NOT EXISTS idx_machines_machine_id ON machines(machine_id);
CREATE INDEX IF NOT EXISTS idx_machines_protected ON machines(protected);

-- 显示表结构
\d machines;