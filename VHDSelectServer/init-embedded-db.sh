#!/bin/bash

# 内置PostgreSQL数据库初始化脚本
# 此脚本在Docker容器启动时运行，用于初始化内置PostgreSQL数据库

set -e

# 日志函数
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# 数据库配置
DB_NAME="${DB_NAME:-vhd_select}"
DB_USER="${DB_USER:-postgres}"
DB_PASSWORD="${DB_PASSWORD:-vhd_select_password}"
PGDATA="${PGDATA:-/var/lib/postgresql/data}"

log "开始初始化内置PostgreSQL数据库..."

# 检查数据目录是否已初始化
if [ ! -s "$PGDATA/PG_VERSION" ]; then
    log "初始化PostgreSQL数据目录..."
    
    # 初始化数据库
    su-exec postgres initdb -D "$PGDATA" --auth-local=trust --auth-host=md5
    
    # 启动PostgreSQL服务
    log "启动PostgreSQL服务..."
    su-exec postgres pg_ctl -D "$PGDATA" -l "$PGDATA/postgresql.log" start
    
    # 等待PostgreSQL启动
    log "等待PostgreSQL服务启动..."
    for i in {1..30}; do
        if su-exec postgres pg_isready -q; then
            log "PostgreSQL服务已启动"
            break
        fi
        if [ $i -eq 30 ]; then
            log "错误: PostgreSQL服务启动超时"
            exit 1
        fi
        sleep 1
    done
    
    # 创建数据库和用户
    log "创建数据库和用户..."
    su-exec postgres psql -v ON_ERROR_STOP=1 <<-EOSQL
        CREATE USER $DB_USER WITH PASSWORD '$DB_PASSWORD';
        CREATE DATABASE $DB_NAME OWNER $DB_USER;
        GRANT ALL PRIVILEGES ON DATABASE $DB_NAME TO $DB_USER;
EOSQL
    
    # 执行初始化SQL脚本
    if [ -f "/app/init-db.sql" ]; then
        log "执行数据库初始化脚本..."
        su-exec postgres psql -v ON_ERROR_STOP=1 -d "$DB_NAME" -f "/app/init-db.sql"
        log "数据库初始化脚本执行完成"
    else
        log "警告: 未找到数据库初始化脚本 /app/init-db.sql"
    fi
    
    # 停止PostgreSQL服务
    log "停止PostgreSQL服务..."
    su-exec postgres pg_ctl -D "$PGDATA" stop
    
    log "内置PostgreSQL数据库初始化完成"
else
    log "PostgreSQL数据目录已存在，跳过初始化"
fi

log "内置数据库初始化脚本执行完成"