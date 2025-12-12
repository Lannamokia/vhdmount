# VHD选择服务器（VHDSelectServer）

现代化的跨平台 VHD 管理服务器，提供 Web GUI 与 HTTP API。支持机台管理、EVHD 密码下发（RSA‑OAEP‑SHA1）、公钥注册审批，以及内置/外部 PostgreSQL 持久化。
<img width="1280" height="1754" alt="image" src="https://github.com/user-attachments/assets/c1126381-d13e-4e88-a8da-99f86d778bef" />

## ✨ 特性

- 🌐 跨平台：Windows / Linux / macOS
- 🎨 现代化 Web 界面：响应式管理页与机台页
- 🔧 RESTful API：标准接口，前后端统一
- 💾 持久化存储：内置或外部 PostgreSQL
- 🔒 EVHD 安全下发：RSA‑OAEP‑SHA1 封装信封
- 🗝️ 公钥注册与审批/吊销：设备密钥生命周期管理
- 🚀 零配置启动：Docker 一条命令可用
- 📈 健康检查与日志：`/api/status` 端点

## 🛠️ 系统要求

### Node.js（推荐）
- Node.js 18+（含 npm）

### Docker（可选）
- Docker 20.10+ / Compose 2+

## 🚀 快速开始

### 方法1：Docker 部署（推荐）

#### 选项1：使用内置数据库（默认）
```bash
# 使用Docker Compose启动（内置PostgreSQL数据库）
docker-compose up -d

# 查看日志
docker-compose logs -f

# 停止服务
docker-compose down
```

#### 选项2：使用外部数据库
```bash
# 使用外部数据库配置文件
docker-compose -f docker-compose.external-db.yml up -d

# 或者手动配置环境变量
docker run -d \
  --name vhd-select-server \
  -p 8080:8080 \
  -v $(pwd)/config:/app/config \
  -v $(pwd)/vhd-data:/app/vhd-data \
  -e USE_EMBEDDED_DB=false \
  -e DB_HOST=your-db-host \
  -e DB_PORT=5432 \
  -e DB_NAME=vhd_select \
  -e DB_USER=your-db-user \
  -e DB_PASSWORD=your-db-password 
  lty271104/vhd-select-server:latest
```

### 方法2：Node.js 直接运行

1. **安装Node.js**
   - 访问 [https://nodejs.org/](https://nodejs.org/)
   - 下载并安装 LTS 版本（18+）

2. **启动服务器**
   ```bash
   # Windows 双击运行
   start.bat

   # 或者命令行运行
   npm install
   npm start
   ```


## 🌐 访问界面

服务器启动后，访问以下地址：

- Web 管理界面：`http://localhost:8080`
- 状态/版本信息：`http://localhost:8080/api/status`

## 📡 API 接口

提供完整的 RESTful API，覆盖机台管理、VHD 关键词设置、EVHD 密码下发与公钥生命周期管理。

### 🔐 认证接口

#### 登录
```http
POST /api/auth/login
Content-Type: application/json

{
  "password": "admin123"
}
```

**响应示例**:
```json
{
  "success": true,
  "message": "登录成功"
}
```

#### 登出
```http
POST /api/auth/logout
```

**响应示例**:
```json
{
  "success": true,
  "message": "已登出"
}
```

#### 检查认证状态
```http
GET /api/auth/check
```

**响应示例**:
```json
{
  "isAuthenticated": true
}
```

#### 修改管理员密码
```http
POST /api/auth/change-password
Content-Type: application/json
Authorization: 需要登录

{
  "currentPassword": "admin123",
  "newPassword": "newPassword123",
  "confirmPassword": "newPassword123"
}
```

**响应示例**:
```json
{
  "success": true,
  "message": "密码修改成功"
}
```

**错误响应示例**:
```json
{
  "success": false,
  "message": "当前密码错误"
}
```

### 🖥️ VHD 关键词接口

#### 获取当前VHD关键词
```http
GET /api/boot-image-select?machineId=MACHINE001
```

**参数说明**:
- `machineId` (必需): 机台唯一标识符

**响应示例**:
```json
{
  "success": true,
  "BootImageSelected": "SDEZ",
  "machineId": "MACHINE001",
  "timestamp": "2025-10-21T11:39:43.809Z"
}
```

#### 设置全局VHD关键词 🔒
```http
POST /api/set-vhd
Content-Type: application/json
Authorization: 需要登录

{
  "BootImageSelected": "NEW_KEYWORD"
}
```

**响应示例**:
```json
{
  "success": true,
  "BootImageSelected": "NEW_KEYWORD",
  "message": "VHD关键词更新成功"
}
```

### 🛡️ 机台保护接口

#### 获取机台保护状态
```http
GET /api/protect?machineId=MACHINE001
```

**参数说明**:
- `machineId` (必需): 机台唯一标识符

**响应示例**:
```json
{
  "success": true,
  "protected": false,
  "machineId": "MACHINE001",
  "timestamp": "2025-10-21T11:39:43.809Z"
}
```

#### 设置机台保护状态 🔒
```http
POST /api/protect
Content-Type: application/json
Authorization: 需要登录

{
  "machineId": "MACHINE001",
  "protected": true
}
```

**响应示例**:
```json
{
  "success": true,
  "protected": true,
  "machineId": "MACHINE001",
  "message": "机台保护状态已更新"
}
```

### 🏭 机台管理接口

#### 获取所有机台信息 🔒
```http
GET /api/machines
```
Authorization: 需要登录
```

**响应示例**:
```json
{
  "success": true,
  "machines": [
    {
      "machine_id": "MACHINE001",
      "protected": false,
      "vhd_keyword": "SDEZ",
      "last_seen": "2025-10-21T11:39:43.809Z",
      "created_at": "2025-10-21T10:00:00.000Z"
    }
  ],
  "count": 1,
  "timestamp": "2025-10-21T11:39:43.809Z"
}
```

#### 设置特定机台的VHD关键词 🔒
```http
POST /api/machines/{machineId}/vhd
Content-Type: application/json
Authorization: 需要登录

{
  "vhdKeyword": "CUSTOM_VHD"
}
```

**响应示例**:
```json
{
  "success": true,
  "machineId": "MACHINE001",
  "vhdKeyword": "CUSTOM_VHD",
  "message": "机台VHD关键词已更新"
}
```

#### 删除机台 🔒
```http
DELETE /api/machines/{machineId}
Authorization: 需要登录
```

**响应示例**:
```json
{
  "success": true,
  "machineId": "MACHINE001",
  "message": "机台已删除"
}
```

### 📊 系统状态接口

#### 获取服务器状态
```http
GET /api/status
```

**响应示例**:
```json
{
  "success": true,
  "status": "running",
  "BootImageSelected": "SDEZ",
  "uptime": 30.415774368,
  "timestamp": "2025-10-21T11:39:43.809Z",
  "version": "1.2.1"
}
```

### 📝 API使用说明

**认证要求**:
- 🔒 标记的接口需要先通过 `/api/auth/login` 登录
- 登录后会话有效期为24小时
- 默认管理员密码: `admin123`

**错误响应格式**:
```json
{
  "success": false,
  "error": "错误描述",
  "requireAuth": true  // 仅在需要认证时出现
}
```

**状态码说明**:
- `200`: 请求成功
- `400`: 请求参数错误
- `401`: 未认证或认证失败
- `404`: 资源不存在
- `500`: 服务器内部错误

## 🎯 使用说明

### Web界面操作

1. **查看当前状态**: 页面会显示服务器运行状态和当前VHD关键词
2. **设置新关键词**: 在输入框中输入新的VHD关键词，点击"更新"按钮
3. **自动刷新**: 页面每30秒自动刷新状态
4. **实时反馈**: 操作结果会立即显示在界面上

### 数据持久化

- 内置数据库：容器内 PostgreSQL + 命名卷持久化；首次启动自动执行 `init-db.sql` 初始化表结构。
- 外部数据库：通过环境变量连接外部 PostgreSQL，建议启用备份与访问控制。

### Docker 相关

#### 数据库配置选项

**内置数据库模式（默认）**
- 自动在容器内启动PostgreSQL数据库
- 数据持久化到Docker卷
- 零配置，开箱即用

**外部数据库模式**
- 连接到外部PostgreSQL数据库
- 支持云数据库服务
- 更好的扩展性和可维护性

#### 环境变量

**应用配置**
- `PORT`: 服务端口（默认: 8080）
- `NODE_ENV`: 运行环境（默认: production）

**数据库配置**
- `USE_EMBEDDED_DB`: 是否使用内置数据库（默认: true）
- `DB_HOST`: 数据库主机地址（默认: localhost）
- `DB_PORT`: 数据库端口（默认: 5432）
- `DB_NAME`: 数据库名称（默认: vhd_select）
- `DB_USER`: 数据库用户名（默认: postgres）
- `DB_PASSWORD`: 数据库密码（默认: vhd_select_password）
- `DB_MAX_CONNECTIONS`: 最大连接数（默认: 20）
- `DB_IDLE_TIMEOUT`: 空闲连接超时时间（默认: 30000ms）
- `DB_CONNECTION_TIMEOUT`: 连接超时时间（默认: 5000ms）
- `DB_SSL`: 是否启用SSL连接（默认: false）

#### 数据卷
- `/app/vhd-data`: 业务数据目录（可选）
- `/var/lib/postgresql/data`: 内置数据库数据持久化目录

#### 健康检查
- 端点: `http://localhost:8080/api/status`
- 间隔: 30秒
- 超时: 10秒
- 重试: 3次
- 启动等待时间: 60秒

#### 配置文件说明

**docker-compose.yml**: 使用内置数据库的默认配置
**docker-compose.external-db.yml**: 使用外部数据库的示例配置

#### 故障排除

**内置数据库问题**
```bash
# 检查数据库初始化日志
docker-compose logs vhd-select-server | grep -i postgres

# 重新初始化数据库
docker-compose down -v
docker-compose up -d
```

**外部数据库连接问题**
```bash
# 测试数据库连接
docker run --rm -it postgres:15-alpine psql -h your-db-host -U your-db-user -d vhd_select

# 检查网络连接
docker-compose exec vhd-select-server ping your-db-host
```

**数据持久化问题**
```bash
# 检查数据卷
docker volume ls | grep vhd

# 备份数据
docker run --rm -v vhd_db_data:/data -v $(pwd):/backup alpine tar czf /backup/vhd_backup.tar.gz /data
```

### 命令行测试

```bash
# 获取当前VHD关键词
curl http://localhost:8080/api/boot-image-select

# 设置新的VHD关键词
curl -X POST http://localhost:8080/api/set-vhd \
     -H "Content-Type: application/json" \
     -d '{"BootImageSelected":"CUSTOM_VHD"}'
```

## 📁 文件结构

```
VHDSelectServer/
├── server.js              # Node.js 服务入口
├── database.js           # PostgreSQL 访问层
├── package.json          # Node.js 依赖配置
├── Dockerfile            # 镜像构建（含内置 DB）
├── docker-entrypoint.sh  # 容器入口与 DB 初始化/权限修复
├── init-db.sql           # 表结构初始化
├── start.bat             # Windows 启动脚本
├── public/
│   ├── index.html        # 仪表盘
│   └── machines.html     # 机台管理
└── README.md             # 说明文档
```

## 🔧 配置说明

- 端口：默认 8080，可通过环境变量 `PORT` 修改
- 日志：控制台输出访问日志与数据库连接信息
- 安全：管理员密码默认 `admin123`，部署后请立即修改

## 🔗 与 VHD 挂载程序集成

此服务器与原有的VHD挂载程序完全兼容：

1. **API兼容**: 保持原有的`/api/boot-image-select`接口
2. **数据格式**: 返回相同的JSON格式
3. **配置持久化**: VHD关键词会自动保存和恢复

## 🛡️ 安全特性

- **输入验证**: 自动验证和清理用户输入
- **CORS支持**: 支持跨域请求
- **错误处理**: 完善的错误处理和用户反馈
- **格式标准化**: 自动转换为大写格式
- **用户认证**: 基于会话的管理员认证系统
- **密码安全**: bcrypt加密存储，支持在线修改管理员密码
- **会话管理**: 24小时会话有效期，自动登出保护
- **数据库安全**: 密码哈希持久化存储，支持密码策略验证
- **EVHD 加密**：设备公钥加密使用 `RSA‑OAEP‑SHA1`（适配 Windows 10 LTSC 1809 的 PCP/TPM 支持）

## 🐛 故障排除

### 端口被占用
```bash
# 检查端口占用
netstat -ano | findstr :8080

# 使用其他端口```

### Node.js 未安装
- 下载安装: https://nodejs.org/ （选择 18+ LTS）

### 权限与数据卷问题
- 运行时挂载的持久化卷可能覆盖镜像权限设置；容器启动时入口脚本会修复权限（`docker-entrypoint.sh`）。请确保主机卷目录的读写权限正确。

### EVHD 密文解密失败（客户端）
- 确认服务器端与客户端统一为 `RSA‑OAEP‑SHA1`，并已重启服务端使变更生效。
- 若曾更换设备密钥，删除旧公钥记录后重新注册再获取新密文。
- 在客户端执行 `Get-Tpm` 确认 TPM 正常；使用 `certutil -csp "Microsoft Platform Crypto Provider" -key` 查看设备密钥容器。

## 📝 更新日志

### v1.2.0
- ✅ 完整Docker化支持（内置/外部数据库）
- ✅ PostgreSQL数据库集成
- ✅ 机台管理功能
- ✅ 机台保护状态控制
- ✅ 用户认证和会话管理
- ✅ 管理员密码在线修改功能
- ✅ bcrypt密码加密存储
- ✅ 密码安全策略验证
- ✅ 完整的RESTful API文档
- ✅ 健康检查和监控
- ✅ 数据持久化和备份

### v1.0.0
- ✅ 跨平台支持（Node.js + Python）
- ✅ 现代化Web界面
- ✅ RESTful API接口
- ✅ 用户自定义VHD关键词
- ✅ 配置持久化存储
- ✅ 实时状态更新

## 🤝 技术支持

如有问题，请检查：
1. 服务器是否正常启动
2. 端口是否被占用
3. 防火墙设置
4. 浏览器控制台错误信息

---

**🎉 享受使用更安全、可维护的 VHD 选择服务器！**
### 🔑 密钥注册与审批

#### 注册设备公钥（公开）
```http
POST /api/machines/{machineId}/keys
Content-Type: application/json

{
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----..."
}
```

#### 审批设备（需登录）
```http
POST /api/machines/{machineId}/approve
```

#### 吊销设备（需登录）
```http
POST /api/machines/{machineId}/revoke
```

### 🔐 EVHD 密码接口

#### 获取 EVHD 密码信封（公开）
```http
GET /api/evhd-envelope?machineId=...
```

说明：服务器使用设备公钥以 `RSA‑OAEP‑SHA1` 加密 EVHD 密码返回密文；客户端用 TPM 私钥解密。

#### 管理用途明文查询（需登录）
```http
GET /api/evhd-password/plain?machineId=...
```
