# VHD Mounter & VHDSelectServer

当前版本：`v1.6.0-preview`

**一套完整的街机游戏 VHD 管理与远程控制解决方案**：包含 Windows 客户端（VHD Mounter）与配套 Web 服务（VHDSelectServer）。支持集中化配置、机台保护、EVHD 密码管理以及自动构建发布。

## 1.6.0 新增功能

### 安全加固与权限制升

- **EVHD 密码安全传递**
  - 密码传递方式改为 stdin（ProcessStartInfo.RedirectStandardInput），避免密码出现在命令行参数中
  - 仅在进程启动立即写入后关闭流，大幅降低敏感信息泄露风险

- **服务端跨域与会话安全**
  - 新增 Origin 白名单校验，带 Origin 请求头的请求必须命中 allowedOrigins 列表
  - 会话 Cookie 设置 SameSite=Strict、Secure 随协议自动开启（防 CSRF 与跨域盗用）
  - OTP 二次验证有效期调整为 60 秒，查询 EVHD 明文前必须完成验证

- **敏感信息日志清理**
  - 新增敏感字段过滤与日志脱敏功能，避免密码、密钥出现在日志文件中
  - Updater 子进程输出透传与错误捕获增强，改善故障诊断

### 服务端功能与模块化增强

- **新增核心模块**
  - `auditLog.js`：操作审计日志记录（机台创建、删除、配置变更、管理员操作）
  - `registrationAuth.js`：机台注册证书鉴权逻辑（验证签名、提取证书信息）
  - `securityStore.js`：管理员密码哈希、Session Secret、TOTP 密钥的安全存储（运行时动态生成，不再硬编码）
  - `validators.js`：统一输入校验（machineId 格式、关键字长度、密码复杂度）

- **机台公钥注册限流**
  - 防止频繁注册请求导致的服务压力，新增速率限制与阈值校验
  - 注册公钥时同步记录证书指纹与主体信息

- **OTP 状态逻辑完善**
  - 确保在缺失 otpVerified 时自动使用 otpVerifiedUntil，以验证时间戳判定有效期
  - 相应的单元测试与集成测试覆盖

### 数据库层全面重构

- **配置规范化与多数据库支持**
  - 移除顶层硬编码的 dbConfig，新增 normalizeDbConfig() 函数对参数进行非空与合法性校验
  - SSL 配置改为 rejectUnauthorized: true（强制校验证书）
  - 新增 withClient() 辅助方法，消除重复的 connect/release 样板代码

- **幂等初始化与字段扩展**
  - initialize() 改为幂等初始化：CREATE TABLE IF NOT EXISTS + ADD COLUMN IF NOT EXISTS
  - machines 表新增 `registration_cert_fingerprint`（VARCHAR 128） 与 `registration_cert_subject`（TEXT）字段，记录注册证书信息
  - 新增索引优化：idx_machines_machine_id、idx_machines_protected、idx_machines_last_seen、idx_machines_cert_fingerprint
  - 新增 update_updated_at_column 触发器，自动维护 updated_at 字段

- **敏感字段保护**
  - 所有 RETURNING 子句改为显式列列表，evhd_password 明文不再出现在查询结果中
  - 改为返回 evhd_password_configured 布尔值，与明文查询逻辑分离

### 加密 VHD 工具优化

- **可执行文件路径解析与挂载点规范化**
  - 新增可执行文件路径解析功能，优化进程查找逻辑
  - 挂载点规范化至绝对路径格式，避免路径解析歧义

- **文件替换与删除支持**
  - 扩展文件替换逻辑，支持文件删除标志（文件清单中标记为 deleted）
  - 优化本地目标目录查找，支持通配符与灵活的路径匹配

- **VHD 文件处理优化**
  - 优化 VHD 与 EVHD 文件的扫描与过滤逻辑
  - 进程输出流处理重构，日志记录性能提升
  - 标准错误捕获与日志化改善，增强调试能力

### 容器与部署改进

- **Docker 数据持久化**
  - PostgreSQL 数据目录改为 `pgdata` 子目录模式（/var/lib/postgresql/data/pgdata），兼容 bind mount 与 named volume
  - 脚本自动规范化路径，处理旧版兼容默认值
  - 新增权限检查与警告机制，防止挂载权限问题导致数据损坏

- **健康检查与自修复**
  - 新增 /api/health 端点，返回 { success, status, version, uptime, timestamp }
  - Docker HEALTHCHECK 指令增强（interval=30s、timeout=10s、start-period=60s）
  - 容器启动脚本增强，新增 repair_public_schema_ownership() 自动修复 PostgreSQL 对象权限

- **Compose 配置标准化**
  - docker-compose.yml 与 docker-compose.external-db.yml 均新增 build: { context: . }
  - 健康检查端点与环境变量明确设置，不再依赖外部镜像

### 平台与工具升级

- **.NET 版本升级至 8.0**
  - VHDMounter、Updater、VHDDebugger 从 net6.0 升级至 net8.0
  - RID 规范化：win10-x64 → win-x64（更广泛的兼容性）
  - 新增 Directory.Build.props，统一 solution 级别的构建属性

- **管理工具替换与扩展**
  - VHDMountAdminTools 全面取代已废弃的 UpdatePackagerGUI
  - Flutter 管理客户端完整集成，支持 TOTP 验证码扫码添加功能
  - vhdmount.sln 新增，统一管理所有 .NET 子项目（VHDMounter、Updater、VHDDebugger、VHDMountAdminTools）

- **CI/CD 流程对齐**
  - GitHub Actions 工作流（build.yml）同步更新，.NET 版本、RID 与产物路径全面同步
  - 构建目标明确化，完整支持自包含单文件发布

### 文档与清理

- **文档补充**
  - 新增 CLI_GUIDE.md，说明命令行工具使用方式
  - VHDSelectServer/DOCKER_TROUBLESHOOTING.md：PostgreSQL bind mount 权限问题根因分析与解决方案
  - 更新主 README 关于 Docker 配置、API 与安全模型的说明

- **仓库清理**
  - 删除已废弃的 UpdatePackagerGUI、VHDSelectServer/public（旧版静态管理页）
  - 删除过时的 CLI 指南与批处理脚本（run_as_admin.bat、setup_task.bat）
  - 优化 .gitignore，补齐 .NET、Node.js、Flutter、Docker、证书密钥等忽略规则

## 组件概览

- VHD Mounter（Windows 客户端）
  - 扫描并挂载 VHD 到 `M:`，自动选择启动目录（`SDHD → bin`，其他 → `package`）。
  - 进程保活与异常重启（`sinmai`、`chusanapp`、`mu3`）。
  - 远程 VHD 关键词获取与机台保护状态查询。
  - 可选 USB 文件优先替换，带总进度与当前文件进度显示。
  - 自更新前置判断：`update_done.flag` 版本 ≥ 清单 `version` 则跳过自更新，直接继续启动。

- VHDSelectServer（管理服务）
  - 锁文件初始化、REST API、审计日志、Session + OTP 二次验证。
  - 机台管理（VHD 关键词、EVHD 密码、保护状态、注册审批）。
  - 预配置证书签名注册、Docker 单容器内置 PostgreSQL 或外部数据库模式。

- Updater（辅助更新器）
  - 完成主程序文件替换；支持管理员自我提升（runas）。
  - 替换失败时延迟到重启生效；成功时在主程序目录拉起 `VHDMounter.exe`。
  - 写入 `update_done.flag = manifest.version`；按阈值规则记录并决策更新（local>min 拒绝、等于跳过、小于更新；无 flag 视为小于）。
  - 文件日志 `updater.log`，10MB 循环覆盖；仅记录不自动拷贝到设备。

- VHDMountAdminTools（离线管理工具）
  - 生成 `manifest.json` 与 `manifest.sig`（RSA‑PSS/SHA‑256 签名）。
  - 生成更新签名密钥对、`trusted_keys.pem` 和预配置注册证书包（`.pfx/.pem/.trust.json`）。
  - 路径规则：`app-update` 使用相对路径；`vhd-data` 使用文件名。
  - 清单包含 `createdAt/expiresAt`（默认有效期 3 天），默认 `minVersion=1.5.0`（向后兼容考虑，可根据需要调整）。

- vhd_mount_admin_flutter（Flutter 管理端）
  - 负责服务初始化、管理员登录、OTP 验证、机台管理、证书管理和审计查看。

## 项目结构

```
vhdmount/
├── VHDSelectServer/              # 管理服务
│   ├── server.js                 # 主服务与路由
│   ├── database.js               # PostgreSQL 持久化
│   ├── Dockerfile                # 镜像构建（含内置 DB）
│   ├── docker-entrypoint.sh      # 容器入口与 DB 初始化
│   └── init-db.sql               # 表结构初始化
├── Updater/                      # 辅助更新器（自我提升、替换与重启主程序）
│   └── Program.cs
├── VHDMountAdminTools/           # 离线管理工具（WPF）
│   ├── MainWindow.xaml           # 打包、签名与注册证书界面
│   └── MainWindow.xaml.cs        # 打包与注册证书逻辑
├── vhd_mount_admin_flutter/      # Flutter 管理客户端（Windows）
├── VHDMounter.csproj             # .NET 8 WPF 客户端
├── VHDManager.cs                 # 客户端核心逻辑
├── Program.cs                    # 单实例入口
├── vhdmonter_config.ini          # 客户端配置
├── build.bat                     # 本地构建脚本（生成自包含单文件并复制到 single 目录）
├── single/                       # 打包输出（VHDMounter.exe、Updater.exe、VHDMountAdminTools.exe）
└── .github/workflows/build.yml   # CI 构建与发布
```

## 快速开始

### 环境要求

- Windows 10 1809+ 或 Windows 11（管理员运行）。
- .NET 8.0 Runtime（客户端）。
- Node.js 18+ 或 Docker 20.10+（服务端）。
- Flutter 3.41+（新的 Windows 管理客户端）。

### 启动服务端

Docker（推荐）：

```powershell
cd VHDSelectServer
docker compose up --build -d
docker compose logs -f
```

如果需要把内置 PostgreSQL 数据持久化到宿主机目录，请映射父目录到 `/var/lib/postgresql/data`，并保持实际数据目录为 `pgdata` 子目录。例如：

```yaml
volumes:
  - ./postgres-data:/var/lib/postgresql/data
environment:
  - POSTGRES_DATA_DIR=/var/lib/postgresql/data/pgdata
```

原因：Docker Desktop/部分 bind mount 文件系统不会正确保留挂载根目录上的 Linux 所有权和 `0700` 权限；PostgreSQL 对真实 cluster 目录有严格权限要求。当前镜像已默认使用 `pgdata` 子目录来兼容 named volume 与大多数 bind mount 场景。

本地开发：

```powershell
cd VHDSelectServer
npm install
npm start
```

首次启动后请先调用 `/api/init/status`、`/api/init/prepare`、`/api/init/complete` 完成初始化；不再存在默认管理员密码。

### 启动客户端

```powershell
# 从 Release 下载并右键“以管理员身份运行”
VHDMounter.exe

# 或从源码发布
dotnet publish VHDMounter.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output ./publish/win-x64 \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false
```

新的管理入口：

```powershell
cd vhd_mount_admin_flutter
flutter run -d windows
```

### VHDMounter离线更新与自更新

1. 使用 VHDMountAdminTools：
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
RegistrationCertificatePath=certs\machine-registration.pfx
RegistrationCertificatePassword=ChangeThisPfxPassword
```

- `EnableRemoteSelection`：启用远程 VHD 选择；关闭时仅本地扫描。
- `BootImageSelectUrl`：远程获取 VHD 关键词的地址，客户端会附带 `machineId` 查询参数。
- `EvhdEnvelopeUrl`：EVHD 密码封装信封获取地址（客户端用 TPM 私钥解密）。
- `MachineId`：机台唯一标识，所有远程 API 需要该参数。
- `EnableProtectionCheck`：定时查询机台保护状态，保护开启时可阻止危险操作。
- `ProtectionCheckUrl`：保护状态查询地址（GET，需要 `machineId`）。
- `ProtectionCheckInterval`：保护状态查询间隔（毫秒）。
- `RegistrationCertificatePath`：预配置注册证书 `.pfx` 路径，用于签名公钥注册请求。
- `RegistrationCertificatePassword`：注册证书密码。

密钥流转：客户端在本机 TPM 中生成/保存 RSA 密钥对，并使用预配置注册证书对 `POST /api/machines/:machineId/keys` 请求签名；管理员审批通过后，客户端从 `GET /api/evhd-envelope` 获取密文并用 TPM 私钥（RSA‑OAEP‑SHA1）解密得到 EVHD 密码。

### 重要说明（EVHD 组件）

- EVHD 挂载相关软件与源代码仅提供给认证合作伙伴，用于受控环境下的机台数据保护等功能。
- 普通用户无需配置或使用 EVHD，请直接将 VHD 文件放入设备（本地或 USB），客户端会自动扫描并挂载使用。
- 本开源仓库不包含 EVHD 挂载相关软件的源代码；如需使用，请联系项目维护方完成合作伙伴认证流程。

### 服务端环境变量

- `PORT`：服务端口（默认 `8080`）。
- `USE_EMBEDDED_DB`：`true` 启用容器内置 PostgreSQL；`false` 使用外部数据库。
- `DB_HOST`、`DB_PORT`、`DB_NAME`、`DB_USER`、`DB_PASSWORD`：连接外部数据库时使用。
- `POSTGRES_DATA_DIR`：内置 PostgreSQL 的真实数据目录，默认 `/var/lib/postgresql/data/pgdata`。
- `CONFIG_ROOT_DIR`、`CONFIG_PATH`：服务配置目录映射（1.6.0 新增），用于持久化管理员配置到宿主机。

## 管理入口

- Flutter 管理端：`vhd_mount_admin_flutter`
  - 初始化向导（管理员密码、Session Secret、数据库、可信证书）。
  - 管理员登录与 OTP 二次验证。
  - 机台新增、删除、保护开关、审批、VHD/EVHD 配置、注册重置、明文读取。
  - 可信注册证书管理与审计查看。

- VHDMountAdminTools：
  - 离线生成更新签名密钥与注册证书包。
  - 生成 `manifest.json` / `manifest.sig`。

## API 速览

- `GET /api/status`：服务器状态与版本。
- `GET /api/health`：容器健康检查端点。
- 初始化：`GET /api/init/status`、`POST /api/init/prepare`、`POST /api/init/complete`。
- 认证：`POST /api/auth/login`、`GET /api/auth/check`、`POST /api/auth/logout`、`POST /api/auth/change-password`、`POST /api/auth/otp/verify`、`GET /api/auth/otp/status`。
- `GET /api/boot-image-select?machineId=...`：获取该机台当前 VHD 关键词；若机台不存在会自动创建记录。
- `POST /api/set-vhd`（需登录）：设置默认 VHD 关键词。
- `GET /api/protect?machineId=...`：查询机台保护状态；机台不存在返回 404。
- `POST /api/protect`（需登录）：更新机台保护状态。
- 机台：`GET /api/machines`（需登录）、`POST /api/machines`（需登录，新增机台）、`DELETE /api/machines/:machineId`（需登录，删除机台）、`POST /api/machines/:machineId/vhd`（需登录）、`POST /api/machines/:machineId/evhd-password`（需登录）、`POST /api/machines/:machineId/keys`（注册公钥，需预配置证书签名）、`POST /api/machines/:machineId/approve`（审批）、`POST /api/machines/:machineId/revoke`（重置注册）。
- 证书与审计：`GET /api/security/trusted-certificates`、`POST /api/security/trusted-certificates`、`DELETE /api/security/trusted-certificates/:fingerprint`、`GET /api/audit`。
- EVHD 密码：`GET /api/evhd-envelope?machineId=...`（RSA 封装信封，公开）、`GET /api/evhd-password/plain?machineId=...&reason=...`（明文查询，需登录 + OTP，仅管理用途）。

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
  - 步骤：恢复依赖 → 构建/测试 → 发布 Windows x64 自包含单文件 → 上传构建产物。
  - `release` 事件：打包 ZIP、生成 `CHECKSUMS.md`（SHA256），上传为 Release 资产。
  - 使用 `softprops/action-gh-release@v2` 上传资产，认证采用 `GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}`。
  - 产物名称与位置：`VHDMounter-win-x64-{version}`，位于 `publish/win-x64/`。

建议在工作流顶部声明：

```yaml
permissions:
  contents: write
```

### 本地构建脚本

- 运行 `build.bat` 执行编译与发布，完成后生成自包含单文件并复制到 `single` 目录：
  - `single\VHDMounter.exe`
  - `single\Updater.exe`
  - `single\VHDMountAdminTools.exe`

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

- 登录与会话：所有管理接口通过初始化向导生成的管理员口令与 Session Secret 保护，不再存在默认管理员密码。生产环境务必启用 HTTPS，并将会话 cookie 运行在受信网络环境中。
- EVHD 密文（RSA 信封）：客户端在本机 TPM 生成 RSA 密钥对，并使用预配置注册证书对注册请求签名；服务端通过 `GET /api/evhd-envelope` 使用设备公钥以 `RSA‑OAEP‑SHA1` 加密 EVHD 密码，客户端用 TPM 私钥解密。
- 明文查询（仅管理用途）：`GET /api/evhd-password/plain?machineId=...&reason=...` 需要登录并先完成 OTP 二次验证，仅用于管理/排障。
- 审批与重置：管理员审批通过后设备才能获取密文。执行“重置注册状态”会删除已注册的公钥并将审批状态重置为未审批，以阻止后续密文下发。
- 最近在线审计：服务端在设备调用 `boot-image-select`、`evhd-envelope` 或注册公钥时写入 `last_seen` 时间，仅用于审计与可观测性，不影响权限判定。
- 数据与日志：服务端仅保存设备公钥与 EVHD 密码（明文）于数据库。请配置数据库访问控制与备份策略；避免在日志中输出敏感数据（例如明文密码、私钥）。

## 许可

MIT License
