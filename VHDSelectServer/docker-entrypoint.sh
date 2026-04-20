#!/bin/bash
set -e

# 日志函数
log() {
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1"
}

normalize_postgres_data_dir() {
    local requested_dir="${POSTGRES_DATA_DIR:-/var/lib/postgresql/data/pgdata}"
    requested_dir="${requested_dir%/}"

    if [ -z "$requested_dir" ] || [ "$requested_dir" = "/var/lib/postgresql/data" ]; then
        requested_dir="/var/lib/postgresql/data/pgdata"
    fi

    export POSTGRES_DATA_DIR="$requested_dir"
    export POSTGRES_DATA_PARENT_DIR="$(dirname "$POSTGRES_DATA_DIR")"
}

normalize_config_dir() {
    local requested_root="${CONFIG_ROOT_DIR:-/app/config}"
    local requested_dir="${CONFIG_PATH:-$requested_root/data}"

    requested_root="${requested_root%/}"
    requested_dir="${requested_dir%/}"

    if [ -z "$requested_root" ]; then
        requested_root="/app/config"
    fi

    if [ -z "$requested_dir" ] || [ "$requested_dir" = "$requested_root" ]; then
        requested_dir="$requested_root/data"
    fi

    export CONFIG_ROOT_DIR="$requested_root"
    export CONFIG_PATH="$requested_dir"
}

log_path_state() {
    local target="$1"
    if [ -e "$target" ]; then
        local owner
        local mode
        owner="$(stat -c '%u:%g' "$target" 2>/dev/null || echo unknown)"
        mode="$(stat -c '%a' "$target" 2>/dev/null || echo unknown)"
        log "路径状态: $target owner=$owner mode=$mode"
    else
        log "路径状态: $target 不存在"
    fi
}

prepare_config_dir() {
    normalize_config_dir

    local config_root_dir="$CONFIG_ROOT_DIR"
    local config_dir="$CONFIG_PATH"
    local probe_file="$config_dir/.nodejs-write-test-$$"
    local legacy_files="server-security.json server-initialized.lock server-pending-init.json server-audit.log vhd-config.json"

    log "使用配置根目录: $config_root_dir"
    log "使用实际配置目录: $config_dir"
    mkdir -p "$config_root_dir" "$config_dir" || true
    chown nodejs:nodejs "$config_root_dir" "$config_dir" 2>/dev/null || true
    chmod 755 "$config_root_dir" 2>/dev/null || true
    chmod 775 "$config_dir" 2>/dev/null || true
    log_path_state "$config_root_dir"
    log_path_state "$config_dir"

    if [ "$config_dir" != "$config_root_dir" ]; then
        for file_name in $legacy_files; do
            local legacy_path="$config_root_dir/$file_name"
            local current_path="$config_dir/$file_name"

            if [ -f "$legacy_path" ] && [ ! -e "$current_path" ]; then
                log "迁移旧版配置文件: $legacy_path -> $current_path"
                mv "$legacy_path" "$current_path"
            elif [ -f "$legacy_path" ] && [ -e "$current_path" ]; then
                log "警告: 旧版根目录和当前子目录同时存在 $file_name，保留子目录版本。"
            fi
        done
    fi

    if ! su-exec nodejs sh -c "touch \"$probe_file\" && rm -f \"$probe_file\""; then
        log "错误: CONFIG_PATH 对容器内 nodejs 用户不可写。OTP 初始化和服务配置保存会尝试在该目录创建 *.tmp 临时文件。"
        log "建议将宿主机目录映射到 CONFIG_ROOT_DIR，让实际配置保持在其子目录 CONFIG_PATH，例如 /app/config/data。"
        log "建议: 1) 优先使用 Docker named volume；2) 如果必须 bind mount，确保宿主机目录允许容器内 nodejs 用户写入；3) Windows/macOS 上优先使用 WSL2/ext4 路径或 Docker Desktop managed volume。"
        exit 1
    fi
}

prepare_embedded_db_dirs() {
    normalize_postgres_data_dir

    log "使用 PostgreSQL 数据目录: $POSTGRES_DATA_DIR"
    mkdir -p "$POSTGRES_DATA_PARENT_DIR" "$POSTGRES_DATA_DIR" /run/postgresql || true
    chown postgres:postgres "$POSTGRES_DATA_PARENT_DIR" "$POSTGRES_DATA_DIR" /run/postgresql 2>/dev/null || true
    chmod 700 "$POSTGRES_DATA_DIR" 2>/dev/null || true
    chmod 775 /run/postgresql || true
    umask 077

    log_path_state "$POSTGRES_DATA_PARENT_DIR"
    log_path_state "$POSTGRES_DATA_DIR"

    local mode
    mode="$(stat -c '%a' "$POSTGRES_DATA_DIR" 2>/dev/null || echo unknown)"
    if [ "$mode" != "700" ] && [ "$mode" != "750" ]; then
        log "警告: PostgreSQL 数据目录权限当前为 $mode。对于宿主机 bind mount，请映射父目录到 /var/lib/postgresql/data，让实际 PGDATA 保持默认子目录 pgdata；Windows 环境优先使用 Docker named volume 或 WSL ext4 路径。"
    fi
}

escape_sql_literal() {
    printf "%s" "$1" | sed "s/'/''/g"
}

run_psql_as_postgres() {
    local database="${1:-postgres}"
    shift || true
    su-exec postgres psql -v ON_ERROR_STOP=1 -d "$database" "$@"
}

repair_public_schema_ownership() {
    local escaped_db_user
    escaped_db_user="$(escape_sql_literal "$DB_USER")"

    log "修复 public schema 对象所有权..."
    run_psql_as_postgres "$DB_NAME" <<EOSQL
GRANT USAGE, CREATE ON SCHEMA public TO "$DB_USER";
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO "$DB_USER";
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO "$DB_USER";

DO \$\$
DECLARE
    table_record RECORD;
    sequence_record RECORD;
    function_record RECORD;
BEGIN
    EXECUTE format('ALTER SCHEMA public OWNER TO %I', '$escaped_db_user');

    FOR table_record IN
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('ALTER TABLE public.%I OWNER TO %I', table_record.tablename, '$escaped_db_user');
    END LOOP;

    FOR sequence_record IN
        SELECT sequencename
        FROM pg_sequences
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('ALTER SEQUENCE public.%I OWNER TO %I', sequence_record.sequencename, '$escaped_db_user');
    END LOOP;

    FOR function_record IN
        SELECT p.proname,
               pg_get_function_identity_arguments(p.oid) AS identity_args
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'public'
    LOOP
        EXECUTE format(
            'ALTER FUNCTION public.%I(%s) OWNER TO %I',
            function_record.proname,
            function_record.identity_args,
            '$escaped_db_user'
        );
    END LOOP;
END
\$\$;
EOSQL
}

run_schema_migrations() {
    log "执行数据库 schema migrations..."
    su-exec nodejs node /app/migrate.js
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
    
    # 持久化卷在运行时挂载会覆盖镜像内权限设置，这里在容器启动时修复权限。
    # 实际 PGDATA 使用子目录，避免把 PostgreSQL cluster 直接放在 bind mount 根目录上。
    prepare_embedded_db_dirs
    
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
    
    # 执行应用内置的 schema_version 迁移。
    run_schema_migrations
    # 对复用的数据卷，按当前 public schema 中真实存在的对象统一修复所有权与权限。
    repair_public_schema_ownership
    
    log "内置数据库初始化完成"
}

# 初始化外部数据库表结构
init_external_db() {
    log "执行外部数据库 schema migrations..."
    run_schema_migrations
    log "外部数据库 schema migrations 完成"
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
prepare_config_dir

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
