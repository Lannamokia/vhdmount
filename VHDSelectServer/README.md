# VHD选择服务器 - 跨平台版本

一个现代化的跨平台VHD关键词管理服务器，提供Web GUI界面和HTTP API接口。

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

1. **一键部署脚本（最简单）**
   ```bash
   # Windows
   deploy.bat
   
   # Linux/macOS
   chmod +x deploy.sh
   ./deploy.sh
   ```

2. **使用Docker Compose**
   ```bash
   # 构建并启动服务
   docker-compose up -d
   
   # 查看日志
   docker-compose logs -f
   
   # 停止服务
   docker-compose down
   ```

3. **使用Docker命令**
   ```bash
   # 运行容器（配置持久化）
   docker run -d \
     --name vhd-select-server \
     -p 8080:8080 \
     -v $(pwd)/config:/app/config \
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

### 方法3: Python版本

```bash
# 直接运行Python服务器
python server.py

# 或指定端口
python server.py 8080
```

## 🌐 访问界面

服务器启动后，访问以下地址：

- **Web界面**: http://localhost:8080
- **API文档**: http://localhost:8080/api/status

## 📡 API接口

### 获取当前VHD关键词
```http
GET /api/boot-image-select
```

**响应示例**:
```json
{
  "success": true,
  "BootImageSelected": "SDEZ",
  "timestamp": "2025-10-19T03:05:33.486615"
}
```

### 设置VHD关键词
```http
POST /api/set-vhd
Content-Type: application/json

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

### 服务器状态
```http
GET /api/status
```

**响应示例**:
```json
{
  "success": true,
  "status": "running",
  "BootImageSelected": "SDEZ",
  "timestamp": "2025-10-19T03:05:33.486615",
  "version": "1.0.0"
}
```

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

#### 环境变量
- `PORT`: 服务端口（默认: 8080）
- `CONFIG_PATH`: 配置文件目录路径（默认: /app/config）
- `NODE_ENV`: 运行环境（默认: production）

#### 数据卷
- `/app/config`: 配置文件持久化目录
- `/app/data`: 数据文件持久化目录（可选）

#### 健康检查
- 端点: `http://localhost:8080/api/status`
- 间隔: 30秒
- 超时: 10秒
- 重试: 3次

#### 故障排除
如果Docker部署遇到问题，请参考 [Docker故障排除指南](DOCKER_TROUBLESHOOTING.md)

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