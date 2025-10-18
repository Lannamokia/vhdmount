# Docker 部署故障排除指南

## 常见问题及解决方案

### 1. Docker镜像拉取失败

**问题**: `failed to resolve source metadata for docker.io/library/node:18-alpine`

**原因**: 网络连接问题或Docker Hub访问受限

**解决方案**:

#### 方案A: 配置Docker镜像源（推荐）
```bash
# 创建或编辑Docker配置文件
# Windows: %USERPROFILE%\.docker\daemon.json
# Linux/macOS: /etc/docker/daemon.json

{
  "registry-mirrors": [
    "https://docker.mirrors.ustc.edu.cn",
    "https://hub-mirror.c.163.com",
    "https://mirror.baidubce.com"
  ]
}

# 重启Docker服务
# Windows: 重启Docker Desktop
# Linux: sudo systemctl restart docker
```

#### 方案B: 使用国内镜像
修改 `Dockerfile` 第一行:
```dockerfile
# 原始
FROM node:18-alpine

# 替换为
FROM registry.cn-hangzhou.aliyuncs.com/library/node:18-alpine
```

#### 方案C: 使用本地Node.js部署
```bash
npm install
npm start
```

### 2. 端口占用问题

**问题**: `port is already allocated`

**解决方案**:
```bash
# 查看端口占用
netstat -ano | findstr :8080  # Windows
lsof -i :8080                 # Linux/macOS

# 停止占用端口的进程
taskkill /PID <PID> /F        # Windows
kill -9 <PID>                 # Linux/macOS

# 或修改端口
docker-compose down
# 编辑 docker-compose.yml 中的端口映射
docker-compose up -d
```

### 3. 权限问题

**问题**: `permission denied`

**解决方案**:
```bash
# Linux/macOS
sudo chown -R $USER:$USER ./config
chmod 755 ./config

# Windows
# 右键config文件夹 -> 属性 -> 安全 -> 编辑权限
```

### 4. 配置文件不持久化

**问题**: 容器重启后配置丢失

**检查**:
```bash
# 确认卷映射
docker-compose ps
docker inspect vhd-select-server

# 检查配置目录
ls -la ./config/
```

**解决方案**:
```bash
# 确保config目录存在
mkdir -p config

# 检查docker-compose.yml中的volumes配置
# 应该包含: - ./config:/app/config
```

### 5. 容器无法启动

**诊断步骤**:
```bash
# 查看容器状态
docker-compose ps

# 查看详细日志
docker-compose logs vhd-select-server

# 进入容器调试
docker-compose exec vhd-select-server sh

# 检查配置
docker-compose config
```

### 6. 网络连接问题

**问题**: 无法访问 http://localhost:8080

**检查**:
```bash
# 确认容器运行状态
docker-compose ps

# 确认端口映射
docker port vhd-select-server

# 测试网络连接
curl http://localhost:8080/api/status
```

## 完全重置

如果遇到无法解决的问题，可以完全重置:

```bash
# 停止并删除所有容器
docker-compose down -v

# 删除本地镜像（如果有）
docker rmi lty271104/vhd-select-server:latest

# 清理Docker缓存
docker system prune -a

# 重新部署
docker-compose up -d
```

## 获取帮助

1. 查看详细日志: `docker-compose logs -f`
2. 检查Docker版本: `docker --version`
3. 检查系统资源: `docker system df`
4. 如果问题持续，请使用本地Node.js部署方式

## 性能优化

### 减少镜像大小
```dockerfile
# 使用多阶段构建
FROM node:18-alpine AS builder
# ... 构建步骤

FROM node:18-alpine AS runtime
# ... 运行时步骤
```

### 加速构建
```bash
# 使用.dockerignore排除不必要文件
# 已包含在项目中

# 利用构建缓存
docker-compose build --parallel
```