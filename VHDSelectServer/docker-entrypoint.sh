#!/bin/bash
set -e

# 日志函数
log() {
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1"
}

# 等待外部数据库连接
wait_for_external_db() {
    log "等待外部数据库连接..."
    local max_attempts=30
    local attempt=1
    
    while [ $attempt -le $max_attempts ]; do
        if pg_isready -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" 2>/dev/null; then
            log "外部数据库连接成功"
            return 0
        fi
        
        log "尝试连接外部数据库 ($attempt/$max_attempts)..."
        sleep 2
        attempt=$((attempt + 1))
    done
    
    log "错误: 无法连接到外部数据库"
    exit 1
}

# 初始化内置PostgreSQL数据库
init_embedded_db() {
    log "初始化内置PostgreSQL数据库..."
    
    # 检查数据目录是否已初始化
    if [ ! -f "$POSTGRES_DATA_DIR/PG_VERSION" ]; then
        log "初始化PostgreSQL数据目录..."
        su-exec postgres initdb -D "$POSTGRES_DATA_DIR" --auth-local=trust --auth-host=md5
        
        # 配置PostgreSQL
        echo "host all all 0.0.0.0/0 md5" >> "$POSTGRES_DATA_DIR/pg_hba.conf"
        echo "listen_addresses = '*'" >> "$POSTGRES_DATA_DIR/postgresql.conf"
        echo "port = $DB_PORT" >> "$POSTGRES_DATA_DIR/postgresql.conf"
        echo "max_connections = 100" >> "$POSTGRES_DATA_DIR/postgresql.conf"
        echo "shared_buffers = 128MB" >> "$POSTGRES_DATA_DIR/postgresql.conf"
        echo "log_statement = 'all'" >> "$POSTGRES_DATA_DIR/postgresql.conf"
        echo "logging_collector = on" >> "$POSTGRES_DATA_DIR/postgresql.conf"
    fi
    
    # 启动PostgreSQL
    log "启动PostgreSQL服务..."
    su-exec postgres pg_ctl -D "$POSTGRES_DATA_DIR" -l "$POSTGRES_DATA_DIR/postgresql.log" start
    
    # 等待PostgreSQL启动
    local max_attempts=30
    local attempt=1
    while [ $attempt -le $max_attempts ]; do
        if su-exec postgres pg_isready -h localhost -p "$DB_PORT" 2>/dev/null; then
            log "PostgreSQL启动成功"
            break
        fi
        log "等待PostgreSQL启动 ($attempt/$max_attempts)..."
        sleep 1
        attempt=$((attempt + 1))
    done
    
    if [ $attempt -gt $max_attempts ]; then
        log "错误: PostgreSQL启动失败"
        log "PostgreSQL日志内容:"
        cat "$POSTGRES_DATA_DIR/postgresql.log" || true
        exit 1
    fi
    
    # 创建数据库用户和数据库
        log "创建数据库用户和数据库..."
        su-exec postgres psql -c "CREATE USER $DB_USER WITH PASSWORD '$DB_PASSWORD';" 2>/dev/null || true
        su-exec postgres psql -c "CREATE DATABASE $DB_NAME OWNER $DB_USER;" 2>/dev/null || true
        su-exec postgres psql -c "GRANT ALL PRIVILEGES ON DATABASE $DB_NAME TO $DB_USER;" 2>/dev/null || true
    
    # 初始化数据库表结构
        log "初始化数据库表结构..."
        if [ -f "/app/init-db.sql" ]; then
            # 使用postgres用户通过本地socket连接执行初始化脚本
            su-exec postgres psql -d "$DB_NAME" -f /app/init-db.sql
            su-exec postgres psql -d "$DB_NAME" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO $DB_USER;"
            su-exec postgres psql -d "$DB_NAME" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO $DB_USER;"
        fi
    
    log "内置数据库初始化完成"
}

# 初始化外部数据库表结构
init_external_db() {
    log "初始化外部数据库表结构..."
    
    # 检查是否需要初始化表结构
    if ! PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT 1 FROM machines LIMIT 1;" 2>/dev/null; then
        log "创建数据库表结构..."
        PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f /app/scripts/init-db.sql
        log "外部数据库表结构初始化完成"
    else
        log "外部数据库表结构已存在，跳过初始化"
    fi
}

# 启动Node.js应用
start_app() {
    log "启动VHD Select Server应用..."
    cd /app
    su-exec nodejs npm start
}

# 优雅关闭处理
cleanup() {
    log "接收到关闭信号，正在优雅关闭..."
    
    # 关闭Node.js应用
    if [ ! -z "$APP_PID" ]; then
        kill -TERM "$APP_PID" 2>/dev/null || true
        wait "$APP_PID" 2>/dev/null || true
    fi
    
    # 如果使用内置数据库，关闭PostgreSQL
    if [ "$USE_EMBEDDED_DB" = "true" ]; then
        log "关闭内置PostgreSQL数据库..."
        su-exec postgres pg_ctl -D "$POSTGRES_DATA_DIR" stop -m fast 2>/dev/null || true
    fi
    
    log "应用已关闭"
    exit 0
}

# 设置信号处理
trap cleanup SIGTERM SIGINT

# 主逻辑
log "VHD Select Server Docker容器启动"
log "USE_EMBEDDED_DB: $USE_EMBEDDED_DB"

if [ "$USE_EMBEDDED_DB" = "true" ]; then
    log "使用内置PostgreSQL数据库"
    init_embedded_db
else
    log "使用外部数据库: $DB_HOST:$DB_PORT/$DB_NAME"
    wait_for_external_db
    init_external_db
fi

# 启动应用（在后台运行以便处理信号）
start_app &
APP_PID=$!

# 等待应用进程
wait "$APP_PID"