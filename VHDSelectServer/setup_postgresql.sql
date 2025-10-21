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
    vhd_keyword VARCHAR(50) DEFAULT 'SDEZ',
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

-- 7. 插入示例数据
INSERT INTO machines (machine_id, protected, vhd_keyword) VALUES
('MACHINE001', false, 'SDEZ'),
('MACHINE002', true, 'SDHC'),
('MACHINE003', false, 'SDXC')
ON CONFLICT (machine_id) DO NOTHING;

-- 8. 显示创建结果
SELECT 'Database setup completed successfully!' as status;
SELECT * FROM machines;