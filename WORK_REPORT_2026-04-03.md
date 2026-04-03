# 工作报告

日期：2026-04-03

## 任务目标

根据安全整改规划，完成仓库内可落地问题的修复、测试与文档同步，并输出最终整改结果。

## 已完成整改

### 1. 构建与工具链修复

- 新增根目录 `NuGet.Config`，清除异常本地源并固定使用官方 NuGet 源，恢复常规 `restore/build`。
- 将主要 .NET 项目从 .NET 6 升级到 .NET 8：
  - `VHDMounter.csproj`
  - `Updater/Updater.csproj`
  - `VHDMountAdminTools/VHDMountAdminTools.csproj`
  - `VHDDebugger.csproj`
- 运行时标识统一从 `win10-x64` 调整为 `win-x64`。
- 修复 `VHDDebugger` 因根目录默认 Compile 通配和共享 `obj` 目录导致的构建冲突，新增 `Directory.Build.props` 并限制编译项。

### 2. 管理服务安全重构

- 重写 `VHDSelectServer/server.js`，引入新的安全模型：
  - 初始化状态机：`/api/init/status`、`/api/init/prepare`、`/api/init/complete`
  - 无默认管理员密码
  - 无固定 Session Secret
  - OTP 二次验证：`/api/auth/otp/verify`、`/api/auth/otp/status`
  - 可信证书管理与审计接口
- 收紧浏览器会话暴露面：仅对白名单 `allowedOrigins` 返回跨域凭据头，未授权 `Origin` 直接拒绝；Session Cookie 调整为 `SameSite=Strict` 与 `secure=auto`。
- 重写 `VHDSelectServer/database.js`，移除弱默认数据库配置，加入注册证书字段与更严格的查询返回。
- 新增并接入以下模块：
  - `VHDSelectServer/securityStore.js`
  - `VHDSelectServer/registrationAuth.js`
  - `VHDSelectServer/auditLog.js`
  - `VHDSelectServer/validators.js`
- 重写 `VHDSelectServer/init-db.sql` 与 `VHDSelectServer/setup_postgresql.sql`，移除默认管理员密码逻辑。
- 删除旧 Web 管理前端静态资源：
  - `VHDSelectServer/public/index.html`
  - `VHDSelectServer/public/machines.html`
  - `VHDSelectServer/public/machines.js`
  - `VHDSelectServer/public/script.js`
  - `VHDSelectServer/public/styles.css`

### 3. 机台注册与客户端改造

- 修改 `VHDManager.cs`，将机台公钥注册从匿名模式升级为“预配置证书签名注册”。
- 修改 `VHDManager.cs`，调用 `encrypted-vhd-mount.exe` 时改为使用 `--password-stdin` 和 UTF-8 标准输入传递 EVHD 密码，消除命令行参数泄露窗口，并补充进程提前退出时的错误诊断。
- 为客户端增加注册证书配置项：
  - `RegistrationCertificatePath`
  - `RegistrationCertificatePassword`
- 修复 .NET 兼容性问题，改为手工导出证书 PEM，避免旧 API 不可用造成构建失败。
- 修复注册签名规范化问题，对签名载荷中的关键字段进行统一 canonicalization，解决客户端/服务端因 PEM 空白差异导致的验签失败。

### 4. 管理工具与新管理端

- 新建 `VHDMountAdminTools/`，替代旧 `UpdatePackagerGUI/`：
  - 生成更新签名密钥
  - 打包并签名 `manifest.json`
  - 生成预配置注册证书包（`.pfx`、`.pem`、trust JSON、客户端配置片段）
- 更新 `vhdmount.sln`、`build.bat`、`.github/workflows/build.yml` 等构建入口与 CI 配置，使其切换到 `VHDMountAdminTools` 与 .NET 8。
- 新建 `vhd_mount_admin_flutter/` Windows Flutter 管理客户端，实现：
  - 连接与初始化向导
  - 管理员登录
  - OTP 验证
  - Dashboard
  - 机器管理
  - 可信证书管理
  - 审计查看
  - 设置管理

### 5. 依赖与文档同步

- 升级 `VHDSelectServer/package.json` 依赖并重新安装，`npm audit` 结果为 0 vulnerabilities。
- 更新根目录 `README.md`，同步以下事实：
  - 管理面已改为 Flutter 客户端
  - 旧 Web GUI 已废弃
  - `UpdatePackagerGUI` 已由 `VHDMountAdminTools` 取代
  - 不再存在默认管理员密码
  - .NET 8 / `win-x64` 为当前构建基线
- 重写 `VHDSelectServer/README.md`，与当前初始化、OTP、证书签名注册模型保持一致。

## 自动化测试与构建验证

以下验证已实际执行并通过：

### Node.js

- 在 `VHDSelectServer` 中执行 `npm test`
- 结果：5/5 通过
- 覆盖内容：
  - 初始化流程
  - Origin 白名单与未授权跨域拒绝
  - OTP 验证
  - 可信证书签名注册
  - nonce 防重放
  - `machineId` 参数校验

### .NET

- 执行 `dotnet build vhdmount.sln`
- 结果：通过
- 在 EVHD 密码传递改为 stdin 后，再次执行 `dotnet build VHDMounter.csproj`
- 结果：通过
- 执行以下发布命令：
  - `dotnet publish VHDMounter.csproj -c Release -r win-x64`
  - `dotnet publish Updater/Updater.csproj -c Release -r win-x64`
  - `dotnet publish VHDMountAdminTools/VHDMountAdminTools.csproj -c Release -r win-x64`
- 结果：全部通过

### Flutter

- 在 `vhd_mount_admin_flutter` 中执行 `flutter analyze`
- 结果：通过
- 执行 `flutter test`
- 结果：通过
- 执行 `flutter build windows`
- 结果：通过，生成 Windows 可执行文件

## 关键产出文件

- `NuGet.Config`
- `Directory.Build.props`
- `VHDSelectServer/server.js`
- `VHDSelectServer/database.js`
- `VHDSelectServer/securityStore.js`
- `VHDSelectServer/registrationAuth.js`
- `VHDSelectServer/auditLog.js`
- `VHDSelectServer/validators.js`
- `VHDSelectServer/test/server.test.js`
- `VHDManager.cs`
- `vhdmonter_config.ini`
- `VHDMountAdminTools/*`
- `vhd_mount_admin_flutter/*`
- `README.md`
- `VHDSelectServer/README.md`

## 遗留事项

当前仓库内本轮整改计划列出的主要问题已完成闭环。后续若继续提升本地机密处理强度，可再评估将 EVHD 密码从托管字符串进一步收敛到更短生命周期的缓冲区表示。

## 结论

本次整改已完成仓库内可实现的主要安全目标、构建升级、管理端替换和自动化验证。当前代码库已从“旧 Web 管理 + 默认密码 + 匿名注册”的模式切换为“初始化引导 + OTP + 可信证书签名注册 + Flutter 管理端”的新模型，且主要构建、发布与测试链路均已通过验证；EVHD 密码本地传递也已从命令行参数改为标准输入。
