# VHD Mounter - 智能SEGA街机游戏VHD挂载管理和运行保活工具

当前版本：v1.2.1

> 🎮 **完整的街机游戏VHD管理解决方案**  
> 包含本地VHD挂载工具和配套的Web服务器，提供集中化配置管理和远程控制功能

## 📦 项目组成

本项目包含两个核心组件：

### 🖥️ VHD Mounter (主程序)
智能VHD挂载和进程保活工具，支持本地和远程配置管理

### 🌐 VHDSelectServer (配套服务器)
基于Node.js的Web服务器，提供：
- 🎯 **集中化VHD选择管理** - Web GUI界面和RESTful API
- 🐳 **Docker化部署** - 支持内置/外部PostgreSQL数据库
- 🔐 **用户认证系统** - 安全的会话管理
- 🛡️ **机台保护功能** - 防止误操作的保护机制
- 📊 **实时状态监控** - 健康检查和系统状态API

## 📁 项目结构

```
vhdmount/
├── 📁 VHDSelectServer/              # Web服务器子项目
│   ├── 📄 server.js                 # 主服务器文件
│   ├── 📄 database.js               # 数据库操作模块
│   ├── 📄 package.json              # Node.js依赖配置
│   ├── 📄 Dockerfile                # Docker镜像构建文件
│   ├── 📄 docker-compose.yml        # Docker Compose配置
│   ├── 📄 docker-compose.external-db.yml  # 外部数据库配置
│   ├── 📄 docker-entrypoint.sh      # Docker启动脚本
│   ├── 📄 init-db.sql               # 数据库初始化脚本
│   ├── 📄 init-embedded-db.sh       # 内置数据库初始化
│   ├── 📄 setup_postgresql.sql      # PostgreSQL设置脚本
│   ├── 📄 README.md                 # VHDSelectServer详细文档
│   ├── 📄 DOCKER_TROUBLESHOOTING.md # Docker故障排除指南
│   ├── 📄 POSTGRESQL_SETUP.md       # PostgreSQL设置指南
│   └── 📁 config/                   # 配置文件目录
├── 📄 VHDMounter.exe                # 主程序可执行文件
├── 📄 VHDMounter.csproj             # .NET项目文件
├── 📄 MainWindow.xaml               # WPF主窗口界面
├── 📄 MainWindow.xaml.cs            # 主窗口逻辑代码
├── 📄 VHDManager.cs                 # VHD管理核心类
├── 📄 Program.cs                    # 程序入口点
├── 📄 vhdmonter_config.ini          # 客户端配置文件
├── 📄 README.md                     # 项目主文档
├── 📄 VHD_Mount_Fix_Guide.md        # VHD挂载故障排除指南
├── 📄 build.bat                     # 构建脚本
├── 📄 debug.bat                     # 调试脚本
├── 📄 run_as_admin.bat              # 管理员运行脚本
└── 📁 .github/workflows/            # GitHub Actions CI/CD配置
    └── 📄 build.yml                 # 自动构建配置
```

### 目录说明

- **VHDSelectServer/**: 独立的Web服务器子项目，提供集中化配置管理
- **主程序文件**: VHD挂载和进程管理的核心Windows应用程序
- **配置文件**: 客户端和服务器的配置管理文件
- **文档**: 详细的使用指南和故障排除文档
- **脚本**: 构建、调试和部署的辅助脚本
- **CI/CD**: 自动化构建和部署配置

## ✨ 核心特性

### 🔍 智能扫描与识别
- **全盘扫描**：自动扫描所有固定磁盘驱动器的根目录
- **关键词过滤**：精确识别包含 `SDEZ`、`SDHD`、`SDDT` 关键词的VHD文件
- **快速定位**：优化的扫描算法，快速定位目标文件

### 🌐 智能网络配置管理
- **VHDSelectServer集成**：配套的Node.js Web服务器，提供完整的集中化配置管理解决方案
- **RESTful API接口**：标准化的HTTP API，支持VHD关键词获取、设置和状态查询
- **Web GUI管理界面**：直观的网页操作界面，支持实时VHD选择和机台保护设置
- **多部署模式支持**：
  - Docker单容器部署（内置PostgreSQL数据库）
  - Docker Compose部署（外部PostgreSQL数据库）
  - 本地开发模式部署
- **配置文件驱动**：通过`vhdmonter_config.ini`灵活控制远程功能开关和服务器地址
- **智能降级机制**：远程获取失败时自动回退到本地选择模式，确保程序稳定运行
- **实时配置同步**：服务器端配置变更立即生效，无需重启客户端程序
- **安全认证机制**：基于会话的用户认证，防止未授权访问和误操作
- **机台保护功能**：支持设置机台保护状态，防止意外的VHD切换操作
- **健康检查监控**：内置健康检查端点，支持服务状态监控和故障诊断

### 🔌 智能USB设备管理
- **NX_INS设备识别**：自动检测卷标为"NX_INS"的USB设备，专为街机系统优化
- **USB优先策略**：优先使用USB设备中的VHD文件，确保使用最新版本
- **智能文件替换**：根据关键词匹配，自动用USB中的VHD文件替换本地对应文件
- **双重扫描机制**：同时扫描USB和本地VHD文件，提供完整的文件管理方案
- **状态实时反馈**：详细显示USB设备检测、文件扫描和替换过程的每个步骤

### 💾 专业VHD管理
- **一键挂载**：自动将VHD文件挂载到M盘
- **智能清理**：挂载前自动分离现有VHD，避免冲突
- **安全卸载**：程序退出时自动解除VHD挂载，保护数据安全

### 🎯 用户友好界面
- **全屏选择**：多VHD文件时提供直观的全屏选择界面
- **实时状态**：操作过程中显示详细的状态信息
- **可视化进度条**：在文件替换阶段显示百分比进度，并同步当前文件/总数
- **步骤暂停**：每个主要执行步骤完成后暂停2秒显示结果，便于用户查看执行状态
- **键盘操作**：支持方向键选择、回车确认、ESC退出

### 🔄 自动化运行
- **程序启动**：根据文件名关键词自动选择启动目录（SDHD → bin，其余 → package），并执行 start.bat（目录名忽略大小写）
- **进程监控**：持续监控 `sinmai`、`chusanapp`、`mu3` 相关进程
- **智能重启**：目标进程异常退出时自动重新启动

### 🛡️ 系统保护
- **关机监听**：监听Windows系统关机事件
- **安全退出**：确保在系统关机前完成VHD卸载操作
- **资源保护**：防止因异常退出导致的VHD资源占用

## 🌐 VHDSelectServer - 集中化配置管理服务器

VHDSelectServer是本项目的配套Web服务器，提供集中化的VHD选择管理和远程控制功能。

### ✨ 主要特性

#### 🎯 集中化VHD管理
- **Web GUI界面**：直观的网页操作界面，支持VHD关键词选择
- **RESTful API**：完整的API接口，支持程序化集成
- **实时配置**：动态更新VHD选择配置，无需重启客户端

#### 🐳 Docker化部署
- **一键部署**：支持Docker和Docker Compose快速部署
- **双数据库模式**：
  - 内置PostgreSQL：单容器部署，适合小规模使用
  - 外部PostgreSQL：分离式部署，适合生产环境
- **健康检查**：内置健康检查机制，确保服务稳定运行

#### 🔐 安全认证系统
- **用户登录**：基于会话的用户认证机制
- **权限控制**：防止未授权访问和误操作
- **会话管理**：安全的会话超时和自动登出
- **密码管理**：
  - bcrypt加密存储，确保密码安全
  - 在线密码修改功能，支持实时更新
  - 密码强度验证，确保安全策略
  - 默认管理员密码：admin123（首次登录后请立即修改）

#### 🛡️ 机台保护功能
- **保护状态**：可设置机台保护状态，防止误操作
- **状态持久化**：保护状态自动保存到数据库
- **API控制**：支持通过API查询和设置保护状态

#### 📊 系统监控
- **状态API**：实时系统状态查询接口
- **健康检查**：服务健康状态监控
- **版本信息**：API版本和系统信息查询

### 🚀 快速部署

#### Docker部署（推荐）

**方式一：内置数据库（单容器）**
```bash
# 构建镜像
docker build -t vhd-select-server ./VHDSelectServer

# 运行容器
docker run -d \
  --name vhd-select-server \
  -p 8080:8080 \
  -v vhd_data:/app/data \
  vhd-select-server
```

**方式二：外部数据库（Docker Compose）**
```bash
# 进入VHDSelectServer目录
cd VHDSelectServer

# 启动服务（包含PostgreSQL）
docker-compose up -d

# 查看服务状态
docker-compose ps
```

#### 🔐 默认登录凭据
Docker容器启动后，可使用以下默认凭据登录Web管理界面：
- **密码**：`admin123`
- **访问地址**：http://localhost:8080

> **⚠️ 安全提醒**：首次登录后请立即修改默认密码！系统支持在线密码修改功能。

#### 本地开发部署
```bash
# 进入VHDSelectServer目录
cd VHDSelectServer

# 安装依赖
npm install

# 启动服务
npm start
```

### 🔧 配置说明

#### 环境变量配置
- `DB_TYPE`：数据库类型（embedded/external）
- `DB_HOST`：数据库主机地址（外部数据库模式）
- `DB_PORT`：数据库端口（默认5432）
- `DB_NAME`：数据库名称（默认vhd_select）
- `DB_USER`：数据库用户名（默认postgres）
- `DB_PASSWORD`：数据库密码（默认postgres）
- `PORT`：服务端口（默认8080）

#### 客户端配置
在主程序目录下的`vhdmonter_config.ini`文件中配置：
```ini
[RemoteSelection]
EnableRemoteSelection=true
BootImageSelectUrl=http://localhost:8080/api/boot-image-select
```

### 📡 API接口

详细的API文档请参考 <mcfile name="README.md" path="VHDSelectServer/README.md"></mcfile>

主要接口包括：
- `GET /api/boot-image-select` - 获取当前VHD选择
- `POST /api/set-vhd` - 设置VHD关键词
- `GET /api/status` - 获取系统状态
- `POST /api/auth/login` - 用户登录
- `GET /api/machines` - 机台管理
- `POST /api/protect` - 设置保护状态

### 🌍 Web界面

访问 `http://localhost:8080` 打开Web管理界面，功能包括：
- VHD关键词选择和设置
- 机台保护状态管理
- 系统状态监控
- 用户登录和会话管理

默认登录密码：`admin123`

## 🚀 快速开始

### 系统要求

- **操作系统**：Windows 10 1903+ / Windows 11
- **运行时**：.NET 6.0 Runtime
- **权限**：管理员权限（VHD操作必需）
- **硬件**：至少1GB可用磁盘空间

### 安装与运行

#### VHD Mounter (主程序)

**方式一：直接运行（推荐）**
1. 下载最新版本的可执行文件
2. 右键点击 → "以管理员身份运行"
3. 程序将自动开始扫描和挂载流程

**方式二：从源码编译**
```powershell
# 克隆仓库
git clone https://github.com/username/vhdmount.git
cd vhdmount

# 编译项目
dotnet build --configuration Release

# 发布独立应用
dotnet publish --configuration Release --self-contained true --runtime win-x64
```

#### VHDSelectServer (配套服务器)

**Docker部署（推荐）**
```powershell
# 进入VHDSelectServer目录
cd VHDSelectServer

# 方式一：单容器部署（内置数据库）
docker build -t vhd-select-server .
docker run -d --name vhd-select-server -p 8080:8080 -v vhd_data:/app/data vhd-select-server

# 方式二：Docker Compose部署（外部数据库）
docker-compose up -d
```

**本地开发部署**
```powershell
# 进入VHDSelectServer目录
cd VHDSelectServer

# 安装Node.js依赖
npm install

# 启动服务器
npm start
```

**配置客户端连接**
在主程序目录创建或编辑 `vhdmonter_config.ini`：
```ini
[RemoteSelection]
EnableRemoteSelection=true
BootImageSelectUrl=http://localhost:8080/api/boot-image-select
```

## 📦 构建与发布

### 客户端（VHD Mounter）
- 发布独立应用（推荐）：
  ```powershell
  dotnet publish VHDMounter.csproj \
    --configuration Release \
    --runtime win-x64 \
    --self-contained true
  ```
- 产物路径：`./VHDMounter/bin/Release/net6.0/win-x64/publish/`
- 版本来源：`VHDMounter.csproj` 和 `app.manifest` 已设置为 `1.2.1`
- 包命名建议：`VHDMounter-v1.2.1-win-x64.zip`
- 运行建议：使用 `run_as_admin.bat` 或右键“以管理员身份运行”

### 服务端（VHDSelectServer）
- Docker 构建与运行（单容器）：
  ```bash
  docker build -t vhd-select-server ./VHDSelectServer
  docker run -d --name vhd-select-server -p 8080:8080 vhd-select-server
  ```
- Docker Compose（外部数据库）：在 `VHDSelectServer` 目录执行 `docker-compose up -d`
- 版本显示：访问 `/api/status`，返回 JSON 中的 `"version": "1.2.1"`

## 📖 使用指南

### 基本操作流程

1. **启动程序**
   - 以管理员身份运行 `VHDMounter.exe`
   - 程序显示全屏白色界面并开始10秒延迟启动
   - 延迟完成后开始主流程扫描

2. **智能扫描过程**
   - **远程VHD选择检查**：
     * 读取`vhdmonter_config.ini`配置文件，检查`EnableRemoteSelection`开关状态
     * 如启用远程功能，向配置的`BootImageSelectUrl`发送HTTP GET请求
     * 解析JSON响应中的`BootImageSelected`字段获取目标关键词
     * 显示远程获取状态和选择的关键词信息
     * 执行完成后暂停2秒显示检查结果
   - **专业USB设备识别**：
     * 自动检测所有可移动设备，精确识别卷标为"NX_INS"的专用USB设备
     * 显示USB设备详细信息（设备名称、路径等）
     * 执行完成后暂停2秒显示检测状态
   - **USB优先文件扫描**：
     * 在检测到的NX_INS USB设备中扫描所有VHD文件
     * 支持根目录下的所有.vhd格式文件识别
     * 实时显示扫描进度和发现的文件数量
     * 扫描完成后暂停2秒显示结果统计
   - **本地VHD文件扫描**：扫描本地VHD文件并显示数量（暂停2秒）
   - **智能文件替换处理**：
     * 基于关键词匹配算法，将USB中的VHD文件与本地文件进行智能配对
     * 自动执行文件替换操作，确保使用最新版本的游戏文件
     * 详细显示每个替换操作的源文件和目标文件信息
     * 替换完成后暂停2秒显示操作结果

3. **VHD文件选择**
   - **远程优先策略**：
     * 如远程获取到有效关键词，自动在本地VHD文件中查找匹配项
     * 基于关键词匹配算法（SDEZ、SDHD、SDDT）进行精确匹配
     * 找到匹配文件时直接启动，无需用户手动选择
     * 未找到匹配时显示提示信息并回退到手动选择模式
   - **本地选择模式**：
     * 单个文件：自动挂载启动
     * 多个文件：提供全屏选择界面，支持 ↑↓ 键选择，Enter 确认

4. **自动挂载与启动**
   - **VHD挂载**：将选定的VHD文件挂载到M盘（暂停2秒显示结果）
   - **目录搜索**：自动搜索启动目录（SDHD → bin，其余 → package，忽略大小写）（暂停2秒显示路径）
   - **程序启动**：在目标目录执行 start.bat 文件（暂停2秒显示结果）

5. **后台监控**
   - 程序最小化到系统托盘
   - 持续监控目标进程状态
   - 异常时自动重启应用

### 键盘快捷键

| 按键 | 功能 |
|------|------|
| ↑↓ | 在选择界面中切换选项 |
| Enter | 确认当前选择 |
| Esc | 安全退出程序 |

#### 文件替换进度条
- 在执行“USB文件替换本地文件”阶段，进度条会从不确定模式切换为可量化模式，显示百分比。
- 总进度按文件数量聚合：已完成文件数/总文件数 + 当前文件进度/总文件数。
- 状态文本实时显示“当前文件 X/Y”和“已复制大小/总大小”，便于定位慢速文件。
- 跳过或校验失败的文件会计入总数但不阻塞后续替换，进度照常推进。
- 替换阶段结束后进度条恢复为不确定模式，用于后续挂载与启动步骤。

### 高级功能

#### 调试模式
程序内置调试功能，可以通过以下方式启用：
```powershell
# 启用详细日志输出
VHDMounter.exe --debug
```

#### 自定义配置
- **远程配置管理**：
  * `EnableRemoteSelection=true/false` - 启用/禁用远程VHD选择功能
  * `BootImageSelectUrl=http://server:port/api/boot-image-select` - 远程服务器API地址
  * 配置文件：`vhdmonter_config.ini`（与程序同目录）
- **本地代码配置**：
  * **目标关键词**：可在源码中修改 `TARGET_KEYWORDS` 数组
  * **监控进程**：可在源码中修改 `PROCESS_KEYWORDS` 数组
  * **挂载盘符**：可在源码中修改 `TARGET_DRIVE` 常量

## 🔧 技术架构

### 整体架构

本项目采用客户端-服务器架构，包含两个核心组件：

```
┌─────────────────────┐    HTTP API    ┌─────────────────────┐
│   VHD Mounter       │◄──────────────►│  VHDSelectServer    │
│   (Windows Client)  │                │   (Web Server)      │
└─────────────────────┘                └─────────────────────┘
│                                       │
├─ VHD挂载管理                          ├─ 集中化配置管理
├─ 进程监控保活                         ├─ Web GUI界面
├─ USB设备检测                          ├─ RESTful API
├─ 远程配置获取                         ├─ 用户认证系统
└─ 本地文件操作                         └─ 数据库持久化
```

### 核心技术栈

#### VHD Mounter (客户端)
- **UI框架**：WPF (.NET 6.0)
- **VHD操作**：Windows DiskPart API
- **进程管理**：System.Diagnostics
- **系统集成**：Microsoft.Win32.SystemEvents
- **异步编程**：async/await 模式
- **HTTP客户端**：HttpClient (.NET 6.0)
- **配置管理**：INI文件解析

#### VHDSelectServer (服务端)
- **运行时**：Node.js 18+
- **Web框架**：Express.js
- **数据库**：PostgreSQL 13+
- **ORM**：原生SQL查询
- **认证**：Express-session
- **容器化**：Docker & Docker Compose
- **前端**：原生HTML/CSS/JavaScript
- **健康检查**：内置健康检查端点

### 关键组件

#### VHDManager 类
- `GetRemoteVHDSelection()` - 远程VHD关键词获取与解析
- `FindVHDByKeyword()` - 基于关键词的VHD文件匹配查找
- `ReadConfig()` - 配置文件读取与解析
- `ScanForVHDFiles()` - 本地VHD文件扫描
- `FindNXInsUSBDrive()` - NX_INS专用USB设备检测与识别
- `ScanUSBForVHDFiles()` - USB设备VHD文件扫描
- `ReplaceLocalVHDFiles()` - 智能文件替换（USB优先策略）
- `MountVHD()` - VHD挂载操作
- `UnmountVHD()` - VHD卸载操作
- `FindFolder(folderName)` - 通用目录定位（忽略大小写）
- `FindPackageFolder()` - Package文件夹定位（对 `FindFolder("package")` 的封装）
- `StartBatchFile()` - 批处理文件启动
- `IsTargetProcessRunning()` - 进程状态检查

#### MainWindow 类
- 用户界面管理
- 系统事件监听
- 应用程序生命周期控制
- 执行步骤状态显示和暂停控制

### EVHD/N 盘与 M 盘工作流
- EVHD 挂载成功的信号：系统出现 `N:` 盘，表示运行时环境准备就绪。
- 游戏 VHD 目标挂载盘符：`M:`，确保在 EVHD 就绪后再进行 `M:` 挂载与后续启动。
- 挂载顺序：检测 `N:` 盘 → 清理旧挂载 → 挂载/分离 VHD 到 `M:` → 搜索启动目录并执行。
- 盘符分配回退：若系统自动分配盘符失败，程序尝试强制分配；如仍失败，优先检查占用进程或磁盘策略。
- 目录搜索策略：`SDHD` → `bin`，其他关键词 → `package`（目录名大小写不敏感）。

#### VHDSelectServer 核心模块

**server.js (主服务器)**
- Express应用初始化和路由配置
- 中间件管理（认证、CORS、静态文件）
- API端点实现和错误处理
- 服务器启动和健康检查

**database.js (数据库层)**
- PostgreSQL连接管理
- 数据库初始化和表创建
- SQL查询封装和事务处理
- 连接池管理和错误恢复

**认证中间件**
- 会话验证和用户状态检查
- 登录状态管理和权限控制
- 安全头设置和CSRF防护

**API路由模块**
- `/api/auth/*` - 用户认证相关接口
- `/api/boot-image-select` - VHD选择获取接口
- `/api/set-vhd` - VHD关键词设置接口
- `/api/machines/*` - 机台管理接口
- `/api/protect` - 保护状态控制接口
- `/api/status` - 系统状态查询接口

### 集成机制

#### 客户端-服务器通信
- **HTTP协议**：基于RESTful API的标准HTTP通信
- **JSON格式**：统一的数据交换格式
- **错误处理**：完整的错误响应和降级机制
- **超时控制**：网络请求超时和重试机制

#### 配置同步
- **实时更新**：服务器配置变更立即生效
- **本地缓存**：客户端配置文件作为备用方案
- **降级策略**：网络异常时自动切换到本地模式

### 安全机制
- **权限检查**：启动时验证管理员权限
- **资源清理**：异常情况下的资源自动释放
- **进程隔离**：独立进程启动，避免相互影响
- **系统集成**：响应系统关机事件，确保数据安全

## ⚠️ 已知限制
- 仅扫描磁盘根目录下的 `.vhd` 文件；子目录中的文件不参与匹配。
- 关键词匹配基于文件名包含 `SDEZ`、`SDHD`、`SDDT`；不解析镜像内部元数据。
- `M:` 盘符被占用会导致挂载失败；建议确保该盘符空闲或调整目标盘符。
- `diskpart` 操作可能被安全策略或杀软拦截；必须以管理员权限运行。
- Docker 首次部署时数据库初始化需要时间；失败时请查看容器日志并重试。
- 远程配置不可用时自动降级到本地模式；此时依赖本地 VHD 文件存在。

## 🐛 故障排除

### 常见问题

**Q: 程序启动后提示权限不足**
A: 确保以管理员身份运行程序，VHD挂载操作需要管理员权限。

**Q: 找不到符合条件的VHD文件**
A: 检查VHD文件名是否包含SDEZ、SDHD或SDDT关键词，且文件位于磁盘根目录。

**Q: VHD挂载失败**
A: 检查M盘是否被其他程序占用，程序会自动尝试清理但可能需要手动处理。

**Q: start.bat启动失败**
A: 确认对应启动目录中存在 start.bat 文件（SDHD: bin；其他关键词: package），且目录名大小写不影响搜索和定位。

**Q: 进程监控不工作**
A: 检查目标程序是否正常运行，进程名是否包含预定义的关键词。

**Q: 程序执行过程中暂停是否正常**
A: 程序在每个主要步骤完成后会自动暂停2秒显示执行结果，这是正常行为。启动时的10秒延迟不受此影响。如需跳过暂停，请等待自动继续。

**Q: 检测不到NX_INS USB设备**
A: 确认USB设备已正确连接且卷标设置为"NX_INS"。可在资源管理器中查看USB设备属性确认卷标。

**Q: USB设备中的VHD文件无法被识别**
A: 确保VHD文件位于USB设备根目录，且文件扩展名为.vhd。程序只扫描根目录下的VHD文件。

**Q: USB文件替换本地文件失败**
A: 检查本地VHD文件是否被其他程序占用，确保有足够的磁盘空间进行文件复制操作。

**Q: 远程VHD选择功能无法工作**
A: 检查`vhdmonter_config.ini`中`EnableRemoteSelection`是否设置为`true`，确认`BootImageSelectUrl`地址正确且服务器可访问。

**Q: 远程服务器连接失败**
A: 确认网络连接正常，检查防火墙设置，验证远程服务器是否正在运行且API接口可用。

**Q: 远程获取的关键词无效**
A: 确认远程服务器返回的JSON格式正确，`BootImageSelected`字段包含有效的关键词（SDEZ、SDHD、SDDT之一）。

**Q: Docker容器无法使用admin123密码登录**
A: 这是v1.2.1之前版本的已知问题，已在最新版本中修复。解决方案：
1. 重新构建Docker镜像：`docker build -t vhd-select-server ./VHDSelectServer`
2. 确认使用最新的`init-db.sql`文件
3. 如果仍有问题，删除旧容器和数据卷后重新部署

**Q: Docker容器启动后Web界面无法访问**
A: 检查以下几点：
1. 确认容器正在运行：`docker ps`
2. 检查端口映射是否正确：`-p 8080:8080`
3. 查看容器日志：`docker logs <container_name>`
4. 确认防火墙没有阻止8080端口

**Q: Docker数据库连接失败**
A: 检查数据库配置：
1. 内置数据库模式：确认容器有足够权限初始化PostgreSQL
2. 外部数据库模式：验证数据库连接参数和网络连通性
3. 查看容器启动日志获取详细错误信息

### 日志和调试

程序运行时会输出详细的调试信息到控制台，可以通过以下方式查看：
1. 在命令行中启动程序
2. 查看控制台输出的调试信息
3. 根据错误信息进行相应的故障排除

## 🤝 贡献指南

我们欢迎社区贡献！请遵循以下步骤：

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

### 开发环境设置
```powershell
# 安装依赖
dotnet restore

# 运行测试
dotnet test

# 启动调试
dotnet run --project VHDMounter.csproj
```

## ⬆️ 升级指南（v1.2.1）
- 客户端（VHD Mounter）：下载最新版本并替换旧 `VHDMounter.exe`；或在源码目录执行发布命令生成安装包。
  ```powershell
  dotnet publish VHDMounter.csproj --configuration Release --runtime win-x64 --self-contained true
  ```
- 服务端（VHDSelectServer）：更新容器或代码版本到 `1.2.1`，重启服务后访问 `/api/status` 验证 `"version": "1.2.1"`。
- 验证：客户端主文档显示“当前版本：v1.2.1”；服务端状态接口返回 `1.2.1`。
- 注意：不需要迁移客户端配置；使用外部数据库的部署保留原连接参数即可。
- CI 提示：建议以 `v1.2.1` tag 触发 GitHub Actions 构建，产出 `VHDMounter-v1.2.1-win-x64.zip`。

## 📝 更新日志

### v1.2.1 (最新)
- 📈 **文件替换阶段可视化进度条**：替换过程显示聚合百分比与当前文件/总数
  - 进度条在替换阶段切换为可量化模式
  - 状态文本同步显示“当前文件 X/Y”与字节级复制进度
  - 跳过/失败文件计入总数但不阻塞流程，替换结束后恢复不确定模式
- 🔧 **Docker密码修复**：修正了Docker容器中默认管理员密码问题
  - 修复了`init-db.sql`中错误的bcrypt哈希值
  - 确保默认密码`admin123`能够正常登录
  - 更新了数据库初始化脚本，使用正确的密码哈希
- ✅ **密码管理增强**：完善了密码安全功能
  - bcrypt加密存储，提高密码安全性
  - 在线密码修改功能，支持实时更新
  - 密码强度验证，确保安全策略
- 📚 **文档更新**：完善了部署和使用文档
  - 添加了默认登录凭据说明
  - 更新了Docker部署指南
  - 增加了密码管理功能说明

### v1.2.0
- ✅ **VHDSelectServer完整Docker化支持**
  - 内置PostgreSQL数据库模式
  - 外部数据库连接支持
  - Docker Compose一键部署
- ✅ **机台管理功能**
  - 机台保护状态控制
  - 状态持久化存储
  - RESTful API接口
- ✅ **用户认证系统**
  - 基于会话的用户认证
  - 安全的密码管理
  - 权限控制机制

### v1.0.0
- ✅ **核心VHD挂载功能**
  - 智能VHD文件检测和挂载
  - 自动化程序启动和进程监控
  - 系统关机安全处理
- ✅ **远程配置支持**
  - RESTful API集成
  - 动态配置更新
  - 跨平台兼容性

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

- 感谢 Microsoft 提供的 .NET 平台和 WPF 框架
- 感谢 Windows DiskPart 工具提供的VHD管理能力
- 感谢所有贡献者和用户的支持

---

**注意**：本工具仅供学习和研究使用，请确保遵守相关法律法规和软件许可协议。