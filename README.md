# VHD Mounter & VHDSelectServer

这是当前仓库状态对应的说明文档。仓库包含 Windows 机台客户端、Node.js 管理服务、离线签名与打包工具、挂载调试工具，以及新的 Flutter 管理客户端。

旧版可操作 Web 管理前端已经移除；VHDSelectServer 根路径 `/` 现在只提供轻量的客户端下载与连接提示页，不再提供可操作的浏览器管理界面。

## 组件概览

- VHDMounter
  - 基于 .NET 8 WPF 的 Windows 客户端，默认发布为 win-x64 自包含单文件。
  - 扫描本地与可移动介质中的 VHD，按配置可向服务端请求目标关键词，并把目标卷绑定到 `M:\`。
  - 依据 VHD 文件名选择启动目录：包含 `SDHD` 时优先查找 `bin`，其他情况查找 `package`。
  - 首次启动使用 `start.bat`；异常重启时优先使用 `start_game.bat`，否则回退到 `start.bat`。
  - 监控目标进程关键字 `sinmai`、`chusanapp`、`mu3`。
  - 可选机台日志上报链路：本地写入 `machine-log-spool.jsonl`，再通过 WebSocket 上传到服务端。

- VHDMounter_Maimoller
  - 由 `EnableHidMenuFeatures=true` 构建出的增强变体。
  - 在基础版之上额外编译 Maimoller HID、系统菜单、系统设置和系统信息采集相关模块。

- VHDSelectServer
  - 基于 Node.js/Express 的管理服务。
  - 提供初始化流程、管理员登录、OTP step-up、机台管理、可信注册证书管理、审计日志和机台日志查询/导出。
  - 支持 Docker 内置 PostgreSQL 或外部 PostgreSQL。
  - 数据库结构通过 `schema_version` + `migrations/` 统一迁移。

- Updater
  - 负责应用离线更新包、替换主程序文件并重新拉起主程序。
  - 会优先尝试重新启动 `VHDMounter_Maimoller.exe`，找不到时再回退到 `VHDMounter.exe`。

- VHDMountAdminTools
  - 离线生成更新签名密钥、`trusted_keys.pem`、`manifest.json`、`manifest.sig`。
  - 生成预配置注册证书包：`.pfx`、`.pem`、`.trust.json` 和可直接粘贴到客户端配置中的 `.client-config.ini` 片段。
  - 默认生成的更新清单有效期为 3 天，默认 `minVersion` 为 `1.5.0`。

- EVHDMountTester
  - 调试用命令行工具，验证 `encrypted-vhd-mount.exe` 挂载 EVHD 到指定挂载点，并可继续验证解密后 VHD 绑定到目标盘符（默认 `M:\`）。

- vhd_mount_admin_flutter
  - 新的管理客户端，负责初始化、登录、OTP 验证、机台管理、证书管理、审计查看和安全设置。
  - 当前工程已包含 Windows、Android 和 iOS 平台骨架；iOS 构建仍需 macOS + Xcode。

## 仓库结构

```text
vhdmount/
├── .github/workflows/           # CI 工作流
├── docs/                        # 规划与实现报告
├── scripts/
│   └── build.bat                # 本地发布脚本
├── src/
│   └── VHDMounter/              # 主客户端源码与默认配置模板
├── Updater/                     # 更新器
├── VHDMountAdminTools/          # 离线管理工具
├── EVHDMountTester/             # EVHD/VHD 挂载调试工具
├── VHDMounter.Tests/            # xUnit 测试
├── VHDSelectServer/             # 管理服务、Docker 与迁移脚本
├── vhd_mount_admin_flutter/     # Flutter 管理客户端
├── artifacts/local-publish/     # 本地脚本发布输出
├── single/                      # 便于拷贝的本地整合输出
├── VHDMounter.csproj            # 主客户端工程
└── vhdmount.sln                 # .NET 解决方案
```

## 环境要求

- Windows 10/11，客户端运行需要管理员权限。
- 从源码构建 .NET 项目需要 .NET 8 SDK。
- 直接运行 VHDSelectServer 需要 Node.js 18+。
- 使用容器部署服务端需要 Docker 20.10+ 和 Compose 2+。
- 外部数据库模式需要 PostgreSQL 15+。
- 开发 Flutter 管理端需要 Flutter stable；工程当前要求满足 `pubspec.yaml` 中的 Dart SDK 约束（`^3.11.4`）。

## 快速开始

### 1. 启动 VHDSelectServer

Docker 部署（推荐）：

```powershell
cd VHDSelectServer
docker compose up --build -d
docker compose logs -f
```

仓库自带的 `docker-compose.yml` 默认把宿主机 `8082` 映射到容器内 `8080`，因此本机访问地址默认是 `http://127.0.0.1:8082`。

如果需要把服务配置持久化到宿主机，请映射父目录到 `/app/config`，让实际可写配置落在子目录 `/app/config/data`：

```yaml
volumes:
  - ./config:/app/config
environment:
  - CONFIG_ROOT_DIR=/app/config
  - CONFIG_PATH=/app/config/data
```

如果需要把内置 PostgreSQL 数据持久化到宿主机，请映射父目录到 `/var/lib/postgresql/data`，不要直接把宿主机目录当作 cluster 根目录使用：

```yaml
volumes:
  - ./postgres-data:/var/lib/postgresql/data
environment:
  - POSTGRES_DATA_DIR=/var/lib/postgresql/data/pgdata
```

本地直接运行：

```powershell
cd VHDSelectServer
npm install
npm run migrate
npm start
```

直接运行时默认监听 `http://127.0.0.1:8080`。

首次部署后的初始化流程：

1. 调用 `GET /api/init/status` 确认是否已初始化。
2. 调用 `POST /api/init/prepare` 获取 OTP 绑定信息。
3. 调用 `POST /api/init/complete` 提交管理员密码、Session Secret、数据库配置、允许的 Origin 和可信注册证书。

推荐使用仓库内的 Flutter 管理客户端完成初始化，而不是手工拼接请求。

### 2. 启动 Windows 客户端与离线工具

从源码本地构建：

```powershell
scripts\build.bat
```

该脚本会：

- 发布基础版 `VHDMounter.exe`
- 发布增强版 `VHDMounter_Maimoller.exe`
- 发布 `Updater.exe`
- 发布 `VHDMountAdminTools.exe`
- 把以上产物与 `vhdmonter_config.ini` 复制到 `single/`
- 同时保留完整发布目录到 `artifacts/local-publish/`

生成后的主程序应以管理员身份运行：

```powershell
single\VHDMounter.exe
single\VHDMounter_Maimoller.exe
```

### 3. 启动 Flutter 管理客户端

```powershell
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d windows
```

附加说明：

- Android 可使用 `flutter run -d android`。
- iOS 工程骨架已在仓库内，但运行和打包仍需 macOS + Xcode。
- Android 模拟器访问本机服务时通常使用 `http://10.0.2.2:端口`；如果服务端按仓库默认 Compose 运行，端口就是 `8082`。

## 客户端配置模板

默认模板位于 `src/VHDMounter/vhdmonter_config.ini`：

```ini
[Settings]
ServerBaseUrl=http://127.0.0.1:8080
EnableRemoteSelection=false
RegistrationCertificatePath=
RegistrationCertificatePassword=
MachineId=MACHINE_001
EnableProtectionCheck=false
ProtectionCheckInterval=500
EnableLogUpload=false
MachineLogUploadIntervalMs=3000
MachineLogUploadBatchSize=200
MachineLogUploadMaxSpoolBytes=52428800
```

说明：

- 构建与发布时复制到输出目录的权威模板来自 `src/VHDMounter/vhdmonter_config.ini`。
- 当前推荐配置项是 `ServerBaseUrl`。客户端会基于它自动派生以下固定端点：
  - `/api/boot-image-select`
  - `/api/evhd-envelope`
  - `/api/protect`
  - `/ws/machine-log`
- 为兼容旧部署，客户端仍能从历史配置项 `BootImageSelectUrl`、`EvhdEnvelopeUrl`、`ProtectionCheckUrl`、`MachineLogServerIp` / `MachineLogServerPort` 反推出服务端基地址，但新的模板不再写这些旧字段。
- 如果你使用仓库自带的 `docker-compose.yml` 默认端口映射，请把 `ServerBaseUrl` 改成 `http://127.0.0.1:8082`，或者自行修改 Compose 端口。
- `EnableLogUpload=true` 时，客户端会在应用目录生成 `machine-log-spool.jsonl` 和 `machine-log-client.log`。

## 客户端当前运行行为

- 目标 VHD 挂载后会绑定到 `M:\`。
- 如果系统先给候选卷分配了其他盘符，客户端会先清理已有盘符，再把卷重新绑定到 `M:\`。
- 目标启动目录按 VHD 文件名决定：`SDHD` 对应 `bin`，其余默认查找 `package`。
- 初次启动使用 `start.bat`；当检测到目标进程退出时，重启顺序为：`start_game.bat` 优先，`start.bat` 兜底。
- 运行日志写入应用目录下的 `vhdmounter.log`，达到 10 MiB 后循环覆盖。
- 若存在卷标为 `NXLOG` 的可移动设备，客户端会把最新日志复制到该设备根目录。

## 离线更新链路

- `VHDMountAdminTools` 用于生成更新签名密钥、`trusted_keys.pem`、更新清单和注册证书包。
- 主程序启动时会检查卷标为 `NX_INS` 的可移动设备，寻找 `manifest.json` / `manifest.sig`（支持根目录或 `updates/` 子目录）。
- 验签通过后，更新文件会被复制到本地 `staging/` 目录，再拉起 `Updater.exe`。
- `Updater` 完成替换后会写入 `update_done.flag`，并重新拉起当前可用的主程序变体。

## 服务端当前状态

- 旧 Web 管理界面已删除，服务根路径 `/` 仅返回下载/连接提示页。
- `GET /api/status` 与 `GET /api/health` 现在只返回最小状态信息：`success`、`status`、`initialized`、`pendingInitialization`、`databaseReady`。
- `GET /api/init/status` 在匿名访问时只返回最小初始化状态；更详细的管理信息只在已登录会话下返回。
- 管理接口采用 Session 登录，敏感操作额外要求 OTP step-up。
- OTP 绑定支持两步式轮换：
  - `POST /api/auth/otp/rotate/prepare`
  - `POST /api/auth/otp/rotate/complete`
- 机台日志链路已落地：服务端支持实时接收、分页查询和导出机台日志，并可配置日志保留策略。
- 数据库结构通过 `migrations/001_*.sql`、`002_*.sql` 之类的版本文件管理；应用启动和 `npm run migrate` 走同一套迁移逻辑。

## 关键接口

公开接口：

- `GET /api/status`
- `GET /api/health`
- `GET /api/init/status`
- `GET /api/boot-image-select?machineId=...`
- `GET /api/protect?machineId=...`
- `GET /api/evhd-envelope?machineId=...`

初始化与认证：

- `POST /api/init/prepare`
- `POST /api/init/complete`
- `POST /api/auth/login`
- `GET /api/auth/check`
- `POST /api/auth/logout`
- `POST /api/auth/change-password`
- `POST /api/auth/otp/verify`
- `GET /api/auth/otp/status`
- `POST /api/auth/otp/rotate/prepare`
- `POST /api/auth/otp/rotate/complete`

设置与机台日志：

- `GET /api/settings/default-vhd`
- `POST /api/settings/default-vhd`
- `POST /api/set-vhd`（兼容旧入口，行为同上）
- `GET /api/settings/log-retention`
- `POST /api/settings/log-retention`
- `GET /api/machine-log-sessions`
- `GET /api/machine-logs`
- `GET /api/machine-logs/export`

机台、证书与审计：

- `GET /api/machines`
- `POST /api/machines`
- `DELETE /api/machines/:machineId`
- `POST /api/machines/:machineId/vhd`
- `POST /api/machines/:machineId/evhd-password`
- `POST /api/machines/:machineId/keys`
- `POST /api/machines/:machineId/approve`
- `POST /api/machines/:machineId/revoke`
- `GET /api/security/trusted-certificates`
- `POST /api/security/trusted-certificates`
- `DELETE /api/security/trusted-certificates/:fingerprint`
- `GET /api/audit`
- `GET /api/evhd-password/plain?machineId=...&reason=...`

说明：证书管理、明文查询、日志导出等高敏感接口要求已登录会话，且部分接口必须先完成 OTP step-up。

## 服务端环境变量

应用与配置目录：

- `PORT`：服务端口，默认 `8080`
- `NODE_ENV`：运行模式
- `CONFIG_ROOT_DIR`：配置根目录
- `CONFIG_PATH`：实际安全配置目录，默认通常设为 `CONFIG_ROOT_DIR` 下的 `data` 子目录
- `MACHINE_REGISTRATION_RATE_LIMIT_MAX`：机台公钥注册接口在 10 分钟窗口内的单机限流阈值，默认 `20`
- `AUDIT_LOG_MAX_BYTES`：单个审计日志文件最大字节数，默认 5 MiB
- `AUDIT_LOG_MAX_FILES`：审计日志最大保留文件数，默认 5

数据库：

- `USE_EMBEDDED_DB`
- `DB_HOST`
- `DB_PORT`
- `DB_NAME`
- `DB_USER`
- `DB_PASSWORD`
- `DB_MAX_CONNECTIONS`
- `DB_IDLE_TIMEOUT`
- `DB_CONNECTION_TIMEOUT`
- `DB_SSL`
- `POSTGRES_DATA_DIR`

## 构建、测试与 CI

本地验证命令：

```powershell
dotnet test VHDMounter.Tests\VHDMounter.Tests.csproj

cd VHDSelectServer
npm test

cd ..\vhd_mount_admin_flutter
flutter analyze
flutter test
```

GitHub Actions：

- `.github/workflows/build.yml`
  - 在 `main`、`dev`、`maimoller_control_test` 分支和所有标签上构建。
  - 产出四个 ZIP：`VHDMounter`、`VHDMounter_Maimoller`、`Updater`、`VHDMountAdminTools`。
  - 标签构建时附带 `CHECKSUMS.sha256` 并上传到 GitHub Release。

- `.github/workflows/flutter-admin-client.yml`
  - 在 Flutter 目录或工作流文件变更时触发。
  - 构建 Windows ZIP、Android release APK、iOS unsigned IPA。
  - 标签构建时汇总产物并上传到 GitHub Release。
  - Android release 签名通过 `ANDROID_KEYSTORE_BASE64`、`ANDROID_KEYSTORE_PASSWORD`、`ANDROID_KEY_ALIAS`、`ANDROID_KEY_PASSWORD` 注入；未配置时会回退到 debug signing 以保证构建可继续完成。

- `.github/workflows/docker-image.yml`
  - 在 `VHDSelectServer/**` 相关变更的 push / pull_request 时构建 Docker 镜像并上传 `.tgz` artifact。
  - 在 GitHub Release 发布事件上构建版本镜像并推送到 DockerHub，同时上传镜像归档 artifact。

## 重要说明

### 重要说明（EVHD 组件）

- EVHD 挂载相关软件与源代码仅提供给认证合作伙伴，用于受控环境下的机台数据保护等功能。
- 普通用户无需配置或使用 EVHD，请直接将 VHD 文件放入设备（本地或 USB），客户端会自动扫描并挂载使用。
- 本开源仓库不包含 EVHD 挂载相关软件的源代码；如需使用，请联系项目维护方完成合作伙伴认证流程。

- 客户端模板中的 `ServerBaseUrl` 默认值是直跑服务的 `http://127.0.0.1:8080`，不是仓库默认 Docker Compose 的宿主机端口。
- 打包后的 .NET 程序默认是 win-x64 自包含单文件，部署时通常不需要额外安装 .NET Runtime。

## 许可

MIT License
