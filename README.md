# VHD Mounter & VHDSelectServer

当前版本：`v1.2.1`

一套完整的街机游戏 VHD 管理与远程控制解决方案：包含 Windows 客户端（VHD Mounter）与配套 Web 服务（VHDSelectServer）。支持集中化配置、机台保护、EVHD 密码管理以及自动构建发布。

## 组件概览

- VHD Mounter（Windows 客户端）
  - 扫描并挂载 VHD 到 `M:`，自动选择启动目录（`SDHD → bin`，其他 → `package`）。
  - 进程保活与异常重启（`sinmai`、`chusanapp`、`mu3`）。
  - 远程 VHD 关键词获取与机台保护状态查询。
  - 可选 USB 文件优先替换，带总进度与当前文件进度显示。

- VHDSelectServer（Web 服务）
  - Web GUI 管理、RESTful API、健康检查与会话认证。
  - 机台管理（VHD 关键词、EVHD 密码、保护状态）。
  - Docker 单容器内置 PostgreSQL 或外部数据库模式（通过环境变量切换）。

## 项目结构

```
vhdmount/
├── VHDSelectServer/              # Web 服务
│   ├── server.js                 # 主服务与路由
│   ├── database.js               # PostgreSQL 持久化
│   ├── public/                   # 管理界面静态文件（index.html、machines.html、*.js、*.css）
│   ├── Dockerfile                # 镜像构建（含内置 DB）
│   ├── docker-entrypoint.sh      # 容器入口与 DB 初始化
│   └── init-db.sql               # 表结构初始化
├── VHDMounter.csproj             # .NET 6 WPF 客户端
├── VHDManager.cs                 # 客户端核心逻辑
├── Program.cs                    # 单实例入口
├── vhdmonter_config.ini          # 客户端配置
└── .github/workflows/build.yml   # CI 构建与发布
```

## 快速开始

### 环境要求

- Windows 10 1903+ 或 Windows 11（管理员运行）。
- .NET 6.0 Runtime（客户端）。
- Node.js 18+ 或 Docker 20.10+（服务端）。

### 启动服务端

Docker（推荐）：

```powershell
docker build -t vhd-select-server ./VHDSelectServer
docker run -d --name vhd-select-server -p 8080:8080 vhd-select-server
start http://localhost:8080
```

本地开发：

```powershell
cd VHDSelectServer
npm install
npm start
```

默认管理员密码：`admin123`（请登录后尽快修改）。

### 启动客户端

```powershell
# 从 Release 下载并右键“以管理员身份运行”
VHDMounter.exe

# 或从源码发布
dotnet publish VHDMounter.csproj --configuration Release --runtime win-x64 --self-contained true
```

## 配置说明

### 客户端（`vhdmonter_config.ini`）

```ini
[Settings]
EnableRemoteSelection=true
BootImageSelectUrl=http://localhost:8080/api/boot-image-select
EvhdPasswordUrl=http://localhost:8080/api/evhd-password
MachineId=MACHINE_001
EnableProtectionCheck=true
ProtectionCheckUrl=http://localhost:8080/api/protect
ProtectionCheckInterval=500
```

- `EnableRemoteSelection`：启用远程 VHD 选择；关闭时仅本地扫描。
- `BootImageSelectUrl`：远程获取 VHD 关键词的地址，客户端会附带 `machineId` 查询参数。
- `EvhdPasswordUrl`：EVHD 密码密文获取地址（客户端以“时区 8 小时 Secret”进行解密）。
- `MachineId`：机台唯一标识，所有远程 API 需要该参数。
- `EnableProtectionCheck`：定时查询机台保护状态，保护开启时可阻止危险操作。
- `ProtectionCheckUrl`：保护状态查询地址（GET，需要 `machineId`）。
- `ProtectionCheckInterval`：保护状态查询间隔（毫秒）。

密钥约定：客户端每小时计算一次 `secret = "evhd" + YYMMDDHH`（UTC+8），服务端用该 `secret` 执行 AES-256-CBC 加密/解密，用于 `/api/evhd-password` 的传输保护。另提供需登录的明文接口 `/api/evhd-password/plain` 以便人工查询与核验。

### 服务端环境变量

- `PORT`：服务端口（默认 `8080`）。
- `USE_EMBEDDED_DB`：`true` 启用容器内置 PostgreSQL；`false` 使用外部数据库。
- `DB_HOST`、`DB_PORT`、`DB_NAME`、`DB_USER`、`DB_PASSWORD`：连接外部数据库时使用。

## 管理界面

- `index.html`（仪表盘）：
  - 服务器状态与当前全局 VHD 关键词。
  - 设置全局 VHD 关键词（需登录）。
  - 机台保护状态查询（无需登录）。
  - 管理入口与管理员密码修改（需登录）。

- `machines.html`（机台管理）：
  - 搜索与刷新机台列表（需登录）。
  - 机台列包含：`ID`、`VHD 关键词`、`EVHD 密码`、`保护状态`、`最后在线`。
  - 设置机台 VHD 关键词与 EVHD 密码；切换保护；删除机台。
  - EVHD 密码查询支持密文接口与明文接口（明文需登录）。

## API 速览

- `GET /api/status`：服务器状态与版本。
- 认证：`POST /api/auth/login`（表单体含 `password`）、`GET /api/auth/check`、`POST /api/auth/logout`、`POST /api/auth/change-password`。
- `GET /api/boot-image-select?machineId=...`：获取该机台当前 VHD 关键词；若机台不存在会自动创建记录。
- `POST /api/set-vhd`（需登录）：设置全局 VHD 关键词。
- `GET /api/protect?machineId=...`：查询机台保护状态；机台不存在返回 404。
- 机台：`GET /api/machines`（需登录）、`POST /api/machines/:machineId/vhd`（需登录）、`POST /api/machines/:machineId/evhd-password`（需登录）。
- EVHD 密码：`GET /api/evhd-password?machineId=...&secret=...`（密文）、`GET /api/evhd-password/plain?machineId=...`（明文，需登录）。

## 使用流程（客户端）

- 启动后读取配置，若启用远程则请求 `BootImageSelectUrl` 获取 `BootImageSelected`。
- 扫描本地与 USB 的 VHD 文件，匹配目标关键词；必要时执行 USB → 本地替换，带总进度与当前文件进度显示。
- 挂载选定 VHD 到 `M:`，搜索启动目录并执行 `start.bat`。
- 后台监控目标进程，异常退出时自动重启。

快捷键：`↑/↓` 切换、`Enter` 确认、`Esc` 退出。

## 构建与发布（CI）

- 工作流：`.github/workflows/build.yml`
  - 触发：`push`、`pull_request`、`release`、`workflow_dispatch`。
  - 步骤：恢复依赖 → 构建/测试 → 发布 Windows x64/x86 自包含单文件 → 上传构建产物。
  - `release` 事件：打包 ZIP、生成 `CHECKSUMS.md`（SHA256），上传为 Release 资产。
  - 使用 `softprops/action-gh-release@v2` 上传资产，认证采用 `GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}`。

建议在工作流顶部声明：

```yaml
permissions:
  contents: write
```

## 故障排除

- 无法挂载或访问盘符：以管理员身份运行；检查安全软件拦截；确认目标盘符未占用。
- 远程 EVHD 密码解密失败：统一客户端与服务端的 UTC+8 小时窗；确保 `machineId` 一致；必要时用 `/api/evhd-password/plain` 验证。
- 服务端无法连接数据库：
  - 内置 DB：确认数据卷权限与初始化日志；
  - 外部 DB：检查 `DB_HOST/PORT/USER/PASSWORD/NAME` 与网络。
- 远程 VHD 关键词获取失败：服务端会返回内存中的 `currentVhdKeyword`；客户端回退到本地扫描模式。

## 安全说明

- 登录态接口通过会话管理保护，默认管理员密码为 `admin123`，请尽快修改。
- EVHD 密文接口提供传输层简易防护（时区密钥），明文接口受登录与权限控制约束。

## 许可

MIT License