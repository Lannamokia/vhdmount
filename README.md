# VHD Mounter & VHDSelectServer

当前版本：`v1.5.0`

## 1.5.0 新增功能

- 离线更新体系全面落地  
  - 介质：NX_INS 根目录直接分发；manifest.json + manifest.sig 与实际更新文件并列放置  
  - 验签：RSA‑PSS/SHA‑256，公钥固定在 trusted_keys.pem  
  - 时效：清单包含 createdAt/expiresAt，主程序更新如过期（>3天）则拒绝更新
- 自更新流程强化  
  - 主程序验签通过后退出，由辅助更新器 Updater 完成文件替换  
  - Updater 支持管理员自我提升（runas）、切换到主程序目录后拉起 VHDMounter.exe  
  - 替换失败则延迟到重启生效；成功时自动拉起主程序
- 版本阈值与更新标记  
  - Updater 通过 update_done.flag 内容记录 manifest.version  
  - 阈值规则：存在 flag 时比较 localVersion 与 minVersion（local>min→拒绝；local=min→跳过；local<min→更新）；无 flag 视为 local<min，直接更新  
  - 主程序启动前置判断：当 flag 版本 >= manifest.version 时不拉起 Updater，直接继续启动
- 新增清单与打包器   
  - 生成 createdAt/expiresAt，默认 minVersion 统一提升为 1.5.0  
  - 使用 RSA‑PSS/SHA‑256 对清单签名
  - 支持快速生成签名密钥对和供机台读取的trusted_keys.pem
- 资源更新逻辑优化  
  - resource_version.tag 缺失时直接触发更新，更新完成后写入 manifest.version  
  - 当存在 tag 时按 minVersion 做阈值判定（local>min 拒绝；local=min 跳过；local<min 更新）
- 日志  
  - 新增 Updater 文件日志 `updater.log`，10MB 循环覆盖；关键步骤（验签、时效、阈值、替换、拉起）均记录  
  - 主程序日志策略保持：`vhdmounter.log` 文件记录，支持 NXLOG 设备拷贝

### 一套完整的街机游戏 VHD 管理与远程控制解决方案：包含 Windows 客户端（VHD Mounter）与配套 Web 服务（VHDSelectServer）。支持集中化配置、机台保护、EVHD 密码管理以及自动构建发布。

## 组件概览

- VHD Mounter（Windows 客户端）
  - 扫描并挂载 VHD 到 `M:`，自动选择启动目录（`SDHD → bin`，其他 → `package`）。
  - 进程保活与异常重启（`sinmai`、`chusanapp`、`mu3`）。
  - 远程 VHD 关键词获取与机台保护状态查询。
  - 可选 USB 文件优先替换，带总进度与当前文件进度显示。
  - 自更新前置判断：`update_done.flag` 版本 ≥ 清单 `version` 则跳过自更新，直接继续启动。

- VHDSelectServer（Web 服务）
  - Web GUI 管理、RESTful API、健康检查与会话认证。
  - 机台管理（VHD 关键词、EVHD 密码、保护状态）。
  - Docker 单容器内置 PostgreSQL 或外部数据库模式（通过环境变量切换）。

- Updater（辅助更新器）
  - 完成主程序文件替换；支持管理员自我提升（runas）。
  - 替换失败时延迟到重启生效；成功时在主程序目录拉起 `VHDMounter.exe`。
  - 写入 `update_done.flag = manifest.version`；按阈值规则记录并决策更新（local>min 拒绝、等于跳过、小于更新；无 flag 视为小于）。
  - 文件日志 `updater.log`，10MB 循环覆盖；仅记录不自动拷贝到设备。

- UpdatePackagerGUI（离线打包器）
  - 生成 `manifest.json` 与 `manifest.sig`（RSA‑PSS/SHA‑256 签名）。
  - 支持快速生成签名密钥对和供机台读取的 `trusted_keys.pem`。
  - 路径规则：`app-update` 使用相对路径；`vhd-data` 使用文件名。
  - 清单包含 `createdAt/expiresAt`（默认有效期 3 天），默认 `minVersion=1.5.0`。

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
├── Updater/                      # 辅助更新器（自我提升、替换与重启主程序）
│   └── Program.cs
├── UpdatePackagerGUI/            # 离线打包器（WPF）
│   ├── MainWindow.xaml           # 打包与签名界面
│   └── MainWindow.xaml.cs        # 打包逻辑（RSA‑PSS签名）
├── VHDMounter.csproj             # .NET 6 WPF 客户端
├── VHDManager.cs                 # 客户端核心逻辑
├── Program.cs                    # 单实例入口
├── vhdmonter_config.ini          # 客户端配置
├── build.bat                     # 本地构建脚本（生成自包含单文件并复制到 single 目录）
├── run_as_admin.bat              # 管理员运行辅助脚本
├── single/                       # 打包输出（VHDMounter.exe、Updater.exe、UpdatePackagerGUI.exe）
└── .github/workflows/build.yml   # CI 构建与发布
```

## 快速开始

### 环境要求

- Windows 10 1809+ 或 Windows 11（管理员运行）。
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
dotnet publish VHDMounter.csproj \
  --configuration Release \
  --runtime win10-x64 \
  --self-contained true \
  --output ./publish/win10-x64 \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false
```

### VHDMounter离线更新与自更新

1. 使用 UpdatePackagerGUI：
   - 选择 payload 目录（更新内容），类型 `app-update` 或 `vhd-data`。
   - 设置 `minVersion`（默认已设为 `1.5.0`）与 `version`。
   - 生成 `manifest.json` 与 `manifest.sig`（RSA‑PSS/SHA‑256）。
2. 分发：
   - 将 `manifest.json`、`manifest.sig` 与实际更新文件一起置于 `NX_INS` 驱动器根目录。
   - 确保主程序目录存在可信公钥列表 `trusted_keys.pem`。
3. 运行：
   - 主程序验签通过且清单未过期（≤3 天）后，判断 `update_done.flag` 与清单版本：
     - `flag >= manifest.version`：跳过自更新，直接启动；
     - 否则复制清单与文件到 `staging` 并拉起 Updater。
   - Updater 完成替换，写入 `update_done.flag = manifest.version`，若未延迟则以管理员在主程序目录拉起 `VHDMounter.exe`。
4. 注意：
   - 过期清单（>3 天）直接拒绝更新；
   - 存在 `update_done.flag` 时按 `localVersion` 与 `minVersion` 阈值判定（local>min 拒绝、等于跳过、小于更新）；无 flag 视为小于直接更新。

## 配置说明

### 客户端（`vhdmonter_config.ini`）

```ini
[Settings]
EnableRemoteSelection=true
BootImageSelectUrl=http://localhost:8080/api/boot-image-select
EvhdEnvelopeUrl=http://localhost:8080/api/evhd-envelope
MachineId=MACHINE_001
EnableProtectionCheck=true
ProtectionCheckUrl=http://localhost:8080/api/protect
ProtectionCheckInterval=500
```

- `EnableRemoteSelection`：启用远程 VHD 选择；关闭时仅本地扫描。
- `BootImageSelectUrl`：远程获取 VHD 关键词的地址，客户端会附带 `machineId` 查询参数。
- `EvhdEnvelopeUrl`：EVHD 密码封装信封获取地址（客户端用 TPM 私钥解密）。
- `MachineId`：机台唯一标识，所有远程 API 需要该参数。
- `EnableProtectionCheck`：定时查询机台保护状态，保护开启时可阻止危险操作。
- `ProtectionCheckUrl`：保护状态查询地址（GET，需要 `machineId`）。
- `ProtectionCheckInterval`：保护状态查询间隔（毫秒）。

密钥流转：客户端在本机 TPM 中生成/保存 RSA 密钥对，将公钥通过 `POST /api/machines/:machineId/keys` 注册到服务端，管理员审批通过后，客户端从 `GET /api/evhd-envelope` 获取密文并用 TPM 私钥（RSA‑OAEP‑SHA1）解密得到 EVHD 密码。

### 重要说明（EVHD 组件）

- EVHD 挂载相关软件与源代码仅提供给认证合作伙伴，用于受控环境下的机台数据保护等功能。
- 普通用户无需配置或使用 EVHD，请直接将 VHD 文件放入设备（本地或 USB），客户端会自动扫描并挂载使用。
- 本开源仓库不包含 EVHD 挂载相关软件的源代码；如需使用，请联系项目维护方完成合作伙伴认证流程。

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
- 机台：`GET /api/machines`（需登录）、`POST /api/machines/:machineId/vhd`（需登录）、`POST /api/machines/:machineId/evhd-password`（需登录）、`POST /api/machines/:machineId/keys`（注册公钥）、`POST /api/machines/:machineId/approve`（审批）、`POST /api/machines/:machineId/revoke`（吊销）。
- EVHD 密码：`GET /api/evhd-envelope?machineId=...`（RSA 封装信封，公开）、`GET /api/evhd-password/plain?machineId=...`（明文查询，需登录，仅管理用途）。

## 使用流程（客户端）

- 启动后读取配置；若启用远程，则请求 `BootImageSelectUrl` 获取目标 VHD 关键词。
- 扫描本地与 USB 的 VHD 文件，匹配目标关键词；必要时执行 USB → 本地替换（仅此阶段显示总进度与当前文件进度）。
- 挂载选定 VHD 到 `M:`，搜索启动目录并执行 `start.bat`；随后监控游戏进程并在异常退出时自动重启（脚本选择策略：如同目录同时存在 `start_game.bat` 与 `start.bat`，优先使用 `start_game.bat`；若仅存在其一则使用该脚本；若两者均不存在则持续等待脚本或进程出现）。

快捷键：`↑/↓` 切换、`Enter` 确认、`Del` 连续三次触发安全退出（2 秒窗口）。

## UI 流程与状态

- 预启动延时：启动后进行 10 秒预备倒计时。
- 准备游戏文件：远程获取关键词与文件扫描。
- 应用本地更新：USB → 本地拷贝，显示进度条（仅此阶段）。
- 准备启动：获取 EVHD 密钥、准备 `start.bat` 环境。
- 更新并启动：执行 `start.bat` 与相关更新；检测到游戏进程后额外等待 5 秒以确保窗口稳定，再切前台到游戏并最小化/隐藏主窗口，避免桌面暴露。
- 异常恢复：游戏崩溃时前台显示恢复状态并尝试重启；重启脚本选择遵循“`start_game.bat` 优先，`start.bat` 兜底”的策略（两者缺失则持续等待）；重启后同样等待 5 秒稳定再切回游戏并收起主窗口。
- 错误提示：发生致命错误时显示“运行发生错误，请联系管理员调阅日志”持续 5 分钟，随后执行系统关机。

## 退出与防暴露策略

- 无关闭按钮；退出通过键盘 `Del` 连续三次（2 秒内）触发安全退出：卸载 VHD、停止 EVHD 加密挂载、退出程序。
- 正常启动与异常恢复均在游戏进程稳定 5 秒后才切前台并收起主窗口，避免桌面环境暴露。

## 日志策略

- 位置：应用目录 `vhdmounter.log`。
- 循环覆盖：超过 10MB 自动截断至 0 并从头继续写入，保留最新日志。
- 设备拷贝：运行时每 5 秒检测卷标为 `NXLOG` 的可移动设备；若存在且日志更新，则复制到设备根目录 `NXLOG:\vhdmounter.log`。每个设备独立记录上次复制时间，避免重复覆盖。
- Updater 日志：应用目录 `updater.log`，10MB 循环覆盖；仅记录，不自动拷贝到设备。

备注：日志仅采用文件记录，UI 展示简洁的阶段性提示。

## 构建与发布（CI）

- 工作流：`.github/workflows/build.yml`
  - 触发：`push`、`pull_request`、`release`、`workflow_dispatch`。
  - 步骤：恢复依赖 → 构建/测试 → 发布 Windows 10 x64 自包含单文件 → 上传构建产物。
  - `release` 事件：打包 ZIP、生成 `CHECKSUMS.md`（SHA256），上传为 Release 资产。
  - 使用 `softprops/action-gh-release@v2` 上传资产，认证采用 `GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}`。
  - 产物名称与位置：`VHDMounter-win10-x64-{version}`，位于 `publish/win10-x64/`。

建议在工作流顶部声明：

```yaml
permissions:
  contents: write
```

### 本地构建脚本

- 运行 `build.bat` 执行编译与发布，完成后生成自包含单文件并复制到 `single` 目录：
  - `single\VHDMounter.exe`
  - `single\Updater.exe`
  - `single\UpdatePackagerGUI.exe`

## 故障排除

- 无法挂载或访问盘符：以管理员身份运行；检查安全软件拦截；确认目标盘符未占用。
- 远程 EVHD 密钥下发失败：确保已在 TPM 生成密钥对并成功注册公钥；确认管理员已审批且未吊销；检查服务器 HTTPS 与时间同步。
- RSA 解密报错（例如 `CryptographicException: NTE_INVALID_PARAMETER`）：客户端与服务端需统一使用 `RSA‑OAEP‑SHA1`；确保服务端已重启使算法变更生效；删除并重新注册设备公钥以重新生成密文。
- TPM 状态核查：在提升的 PowerShell 中执行 `Get-Tpm`，确认 `TpmPresent/Ready=True`；使用 `certutil -csp "Microsoft Platform Crypto Provider" -key` 检查是否存在设备密钥容器（例如 `VHDMounterKey_{machineId}`）。
- 服务端无法连接数据库：
  - 内置 DB：确认数据卷权限与初始化日志；
  - 外部 DB：检查 `DB_HOST/PORT/USER/PASSWORD/NAME` 与网络。
- 远程 VHD 关键词获取失败：服务端会返回内存中的 `currentVhdKeyword`；客户端回退到本地扫描模式。

## 安全说明

- 登录与会话：所有管理接口通过会话保护，默认管理员密码为 `admin123`，请立即修改。生产环境务必设置强随机的 `SESSION_SECRET`，启用 HTTPS 并将会话 cookie 配置为 `secure: true` 与 `httpOnly`。
- EVHD 密文（RSA 信封）：客户端在本机 TPM 生成 RSA 密钥对并注册公钥；服务端通过 `GET /api/evhd-envelope` 使用设备公钥以 `RSA‑OAEP‑SHA1` 加密 EVHD 密码，客户端用 TPM 私钥解密。该接口不需要登录，但需设备已注册公钥且管理员已审批；不再使用旧的基于时区的简易防护方案。
- 明文查询（仅管理用途）：`GET /api/evhd-password/plain?machineId=...` 需要登录，仅用于管理/排障。生产环境建议严格限制或禁用该接口，确保最小权限访问，并强制使用 HTTPS。
- 审批与重置：管理员审批通过后设备才能获取密文。执行“重置注册状态”会删除已注册的公钥并将审批状态重置为未审批，以阻止后续密文下发。
- 最近在线审计：服务端在设备调用 `boot-image-select`、`evhd-envelope` 或注册公钥时写入 `last_seen` 时间，仅用于审计与可观测性，不影响权限判定。
- 数据与日志：服务端仅保存设备公钥与 EVHD 密码（明文）于数据库。请配置数据库访问控制与备份策略；避免在日志中输出敏感数据（例如明文密码、私钥）。

## 许可

MIT License
