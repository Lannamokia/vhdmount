---
layout: default
title: 服务端部署
---

# 服务端部署

VHDSelectServer 提供两种部署方式：Docker（推荐）和本地直接运行。

---

## 方式一：Docker 部署（推荐）

### 前置要求

- Docker 20.10+
- Docker Compose 2+（如使用 Compose 部署）

### 方式 1a：Docker Compose（本地构建）

从仓库源码构建并启动：

```powershell
cd VHDSelectServer
docker compose up --build -d
docker compose logs -f
```

默认访问地址：`http://127.0.0.1:8082`

### 方式 1b：单容器部署（Docker Hub 镜像）

直接使用已发布的 Docker Hub 镜像，无需本地构建：

```powershell
docker run -d \
  --name vhd-select-server \
  -p 8082:8080 \
  -v ./config:/app/config \
  -e CONFIG_ROOT_DIR=/app/config \
  -e CONFIG_PATH=/app/config/data \
  lty271104/vhd-select-server:latest
```

如需使用外部数据库，额外传入数据库环境变量即可。

### 持久化配置

Compose 模式下修改 `docker-compose.yml`：

```yaml
volumes:
  - ./config:/app/config
environment:
  - CONFIG_ROOT_DIR=/app/config
  - CONFIG_PATH=/app/config/data
```

### 持久化数据库

Compose 模式下修改 `docker-compose.yml`：

```yaml
volumes:
  - ./postgres-data:/var/lib/postgresql/data
environment:
  - POSTGRES_DATA_DIR=/var/lib/postgresql/data/pgdata
```

---

## 方式二：本地直接运行

### 前置要求

- Node.js 18+

### 部署步骤

```powershell
cd VHDSelectServer
npm install
npm run migrate
npm start
```

默认访问地址：`http://127.0.0.1:8080`

---

## 首次初始化

部署完成后，需要进行首次初始化：

1. 打开管理客户端或浏览器
2. 访问 `GET /api/init/status` 确认未初始化
3. 按引导完成管理员密码、Session Secret、数据库配置等设置

**推荐使用 Flutter 管理客户端完成初始化**，无需手动拼接请求。
