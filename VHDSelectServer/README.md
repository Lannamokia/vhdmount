# VHD选择服务器 - 跨平台版本

一个现代化的跨平台VHD关键词管理服务器，提供Web GUI界面和HTTP API接口。
<img width="1280" height="1754" alt="image" src="https://github.com/user-attachments/assets/c1126381-d13e-4e88-a8da-99f86d778bef" />

## ✨ 特性

- 🌐 **跨平台支持**: 支持Windows、Linux、macOS
- 🎨 **现代化Web界面**: 响应式设计，支持移动设备
- 🔧 **RESTful API**: 标准HTTP API接口
- 💾 **持久化存储**: 自动保存配置到JSON文件
- 🚀 **零配置启动**: 开箱即用
- 📱 **实时更新**: 界面自动刷新状态
- 🔒 **输入验证**: 安全的用户输入处理

## 🛠️ 系统要求

### Node.js版本（推荐）
- Node.js 14.0+ 
- npm（随Node.js安装）

### Python版本（备选）
- Python 3.6+
- 无需额外依赖

## 🚀 快速开始

### 方法1: Docker部署（推荐）

#### 选项1: 使用内置数据库（默认）
```bash
# 使用Docker Compose启动（内置PostgreSQL数据库）
docker-compose up -d

# 查看日志
docker-compose logs -f

# 停止服务
docker-compose down
```

#### 选项2: 使用外部数据库
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
  -e DB_PASSWORD=your-db-password \
  lty271104/vhd-select-server:latest
```

### 方法2: Node.js版本

1. **安装Node.js**
   - 访问 [https://nodejs.org/](https://nodejs.org/)
   - 下载并安装LTS版本

2. **启动服务器**
   ```bash
   # 双击运行
   start.bat
   
   # 或者命令行运行
   npm install
   npm start
   ```


## 🌐 访问界面

服务器启动后，访问以下地址：

- **Web界面**: http://localhost:8080
- **API文档**: http://localhost:8080/api/status

## 📡 API接口

VHD Select Server 提供完整的RESTful API接口，支持机台管理、VHD关键词设置和状态监控。

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

### 🖥️ VHD关键词接口

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

### 配置持久化

- **Docker部署**: 配置保存在 `/app/config/vhd-config.json`，通过卷映射到主机的 `./config` 目录
- **本地部署**: 配置保存在项目根目录的 `vhd-config.json` 文件中
- **环境变量**: 可通过 `CONFIG_PATH` 环境变量自定义配置文件路径

### Docker相关

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
- `CONFIG_PATH`: 配置文件目录路径（默认: /app/config）
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
- `/app/config`: 配置文件持久化目录
- `/app/vhd-data`: VHD数据文件持久化目录
- `/var/lib/postgresql/data`: 内置数据库数据持久化目录

#### 健康检查
- 端点: `http://localhost:8080/api/health`
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
├── server.js          # Node.js服务器
├── server.py          # Python服务器
├── package.json       # Node.js依赖配置
├── start.bat          # Windows启动脚本
├── public/
│   └── index.html     # Web界面
├── vhd-config.json    # 配置文件（自动生成）
└── README.md          # 说明文档
```

## 🔧 配置说明

- **端口**: 默认8080，可通过环境变量`PORT`修改
- **配置文件**: `vhd-config.json`自动生成，存储当前VHD关键词
- **日志**: 服务器会在控制台输出访问日志

## 🔗 与VHD挂载程序集成

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

## 🐛 故障排除

### 端口被占用
```bash
# 检查端口占用
netstat -ano | findstr :8080

# 使用其他端口
python server.py 8081
```

### Node.js未安装
- 下载安装: https://nodejs.org/
- 使用Python版本作为备选方案

### 权限问题
- 确保有写入配置文件的权限
- 在管理员模式下运行（如需要）

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

**🎉 享受使用全新的跨平台VHD选择服务器！**
