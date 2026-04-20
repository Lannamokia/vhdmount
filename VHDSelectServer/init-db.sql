\set ON_ERROR_STOP on

-- VHD Select Server 数据库初始化脚本
-- 安全初始化（管理员密码、Session Secret、TOTP、可信注册证书）由服务端首次启动流程生成，
-- 本脚本只负责按版本顺序执行 schema migrations。

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    checksum VARCHAR(64) DEFAULT NULL,
    applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

\ir migrations/001_initial_schema.sql
INSERT INTO schema_version (version, name)
VALUES (1, 'initial_schema')
ON CONFLICT (version) DO NOTHING;

\ir migrations/002_machine_security_columns.sql
INSERT INTO schema_version (version, name)
VALUES (2, 'machine_security_columns')
ON CONFLICT (version) DO NOTHING;

\ir migrations/003_machine_log_schema.sql
INSERT INTO schema_version (version, name)
VALUES (3, 'machine_log_schema')
ON CONFLICT (version) DO NOTHING;