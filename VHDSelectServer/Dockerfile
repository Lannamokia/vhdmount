# 使用官方Node.js运行时作为基础镜像
FROM node:18-alpine

# 安装PostgreSQL和相关工具
RUN apk add --no-cache \
    postgresql \
    postgresql-contrib \
    postgresql-client \
    su-exec \
    bash \
    curl

# 设置工作目录
WORKDIR /app

# 复制package.json和package-lock.json（如果存在）
COPY package*.json ./

# 安装依赖
RUN npm ci --only=production

# 复制应用源代码
COPY . .

# 创建必要的目录
RUN mkdir -p /app/config /var/lib/postgresql/data /app/scripts

# 复制数据库初始化脚本
COPY init-db.sql /app/scripts/
COPY docker-entrypoint.sh /app/scripts/
RUN chmod +x /app/scripts/docker-entrypoint.sh
 # 修正Windows换行导致的解释器路径问题
 RUN sed -i 's/\r$//' /app/scripts/docker-entrypoint.sh

# 暴露端口
EXPOSE 8080

# 设置环境变量
ENV NODE_ENV=production
ENV PORT=8080
ENV CONFIG_PATH=/app/config
# 数据库配置环境变量
ENV USE_EMBEDDED_DB=true
ENV DB_HOST=localhost
ENV DB_PORT=5432
ENV DB_NAME=vhd_select
ENV DB_USER=vhd_user
ENV DB_PASSWORD=vhd_password
ENV POSTGRES_DATA_DIR=/var/lib/postgresql/data

# 创建postgres用户和nodejs用户
RUN addgroup -S postgres || true && \
    adduser -S -D -H -h /var/lib/postgresql -s /bin/sh -G postgres postgres || true && \
    addgroup -S nodejs || true && \
    adduser -S nodejs -G nodejs || true

# 创建PostgreSQL数据目录和运行时目录并设置权限
RUN mkdir -p /var/lib/postgresql/data /run/postgresql && \
    chown -R postgres:postgres /var/lib/postgresql /run/postgresql && \
    chmod 700 /var/lib/postgresql/data

# 更改应用文件所有权
RUN chown -R nodejs:nodejs /app

# 启动脚本（覆盖基础镜像的 ENTRYPOINT）
ENTRYPOINT ["/app/scripts/docker-entrypoint.sh"]