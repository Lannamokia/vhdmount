-- PostgreSQL 数据库配置脚本
-- 请使用具有建库权限的账号执行。
-- 管理员密码、TOTP 秘钥、Session Secret 与可信注册证书不在数据库中落盘，
-- 而是由服务端初始化向导写入 server-security.json。
-- 数据库业务 schema 由 init-db.sql 中的 schema_version 迁移清单维护。

CREATE DATABASE vhd_select
    WITH
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'Chinese (Simplified)_China.936'
    LC_CTYPE = 'Chinese (Simplified)_China.936'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

\c vhd_select;

\ir init-db.sql

SELECT 'Database setup completed successfully!' AS status;