# 管理客户端安装

管理客户端基于 Flutter 构建，支持 Windows、Android 和 iOS。

---

## Windows 端

### 方式一：下载预构建版本（推荐）

Windows 版本已编译并上传到 [GitHub Releases](https://github.com/Lannamokia/vhdmount/releases)，下载 `vhd-mount-admin-flutter-windows.zip` 解压后即可运行。

### 方式二：从源码构建

**前置要求：** Flutter stable

```powershell
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d windows
```

---

## Android 端

### 方式一：下载预构建版本（推荐）

Android release APK 已编译并上传到 [GitHub Releases](https://github.com/Lannamokia/vhdmount/releases)，下载 `vhd-mount-admin-flutter-android.apk` 安装即可。

### 方式二：从源码构建

```powershell
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d android
```

> Android 模拟器访问本机服务时，使用 `http://10.0.2.2:8082`（若使用默认 Docker Compose 端口映射）。

---

## iOS 端

### 方式一：下载预构建版本

iOS unsigned IPA 已上传到 [GitHub Releases](https://github.com/Lannamokia/vhdmount/releases)，但**未经签名**，无法直接安装。您可以选择以下任一方式：

- **LiveContainer**：在所有iOS设备上，通过 LiveContainer 直接运行未签名的 IPA，无需越狱，适合临时使用和测试
- **自行签名**：使用Apple Developer 账号或 AltStore、SideStore 等工具对 IPA 进行自签名后安装

### 方式二：从源码构建

iOS 工程骨架已包含在仓库中，构建和运行需要 macOS + Xcode。

```bash
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d ios
```

---

## 管理客户端功能

- 服务端初始化向导
- 管理员登录与 OTP 验证（含 OTP 自动守卫，高敏操作触发时自动弹出验证窗）
- 机台管理与注册证书配置
- 审计日志查看
- 机台日志在线查询与原始文本导出。当前导出结果显示在对话框中，需要手工复制保存，不会自动写入 USB
- 安全设置（密码修改、OTP 轮换、TOTP 密钥管理）
- 部署包上传、部署任务下发、机台部署历史与卸载
- **离线工具（仅 Windows 桌面）**：等价于 `VHDMountAdminTools.exe` 的全部能力
  - 更新签名密钥生成（RSA 3072）
  - 清单打包与签名（RSA-PSS SHA-256）
  - 注册证书包生成（X.509 + PFX + trust.json + client-config.ini）
  - 软件部署本地打包器（`software-deploy` / `file-deploy`）
- 移动端响应式布局，远程管理页面（机台、日志、证书、审计、设置、部署）在窄屏下可正常使用

## 新功能速览

### 离线工具页（仅 Windows 桌面）

仅在 `Platform.isWindows` 且已认证时显示，移动端完全隐藏。包含 4 个标签页：

| 标签页 | 功能 | 说明 |
|--------|------|------|
| 密钥生成 | 生成 RSA 3072 位更新签名密钥对 | 输出 PKCS#8 私钥、SPKI 公钥；自动追加公钥到 `trusted_keys.pem` |
| 清单打包 | 扫描 payload 目录、计算 SHA-256、签名 | RSA-PSS SHA-256 签名，输出 `manifest.json` + `manifest.sig`；`app-update` 类型强制最大 1 GB |
| 证书包 | 生成自签名 X.509 注册证书包 | 输出 `.pfx` / `.pem` / `.trust.json` / `.client-config.ini`；validDays 范围 1–3650 |
| 软件部署打包器 | 直接在管理员电脑上生成 `software-deploy` / `file-deploy` ZIP 与签名 | 与上传功能配合使用 |

操作进行中导航离开离线工具页面时，操作仍会继续执行至完成；用户返回后可看到最近一次的操作结果。

### OTP 自动守卫

高敏操作（如查看证书列表、生成证书、审批机台、查看部署包等）触发时，若 OTP 未验证：

1. 客户端自动弹出 OTP 验证对话框，无需用户手动寻找入口
2. 验证成功后透明地重试原始操作
3. 用户取消时静默返回，不显示额外错误
4. 验证失败时在对话框内显示错误提示，不关闭对话框

### 证书页面"生成证书"按钮（仅 Windows 桌面）

"可信注册证书"页面在 Windows 桌面端的 PageHeader 中新增"生成证书"按钮，与"导入证书"并列：

1. 点击按钮弹出对话框，包含 bundleName、subjectCN、PFX 密码、validDays、输出目录字段
2. 调用 `CertificateGeneratorService` 生成完整证书包到本地
3. 自动调用 `addTrustedCertificate(bundleName, certificatePem)` 导入服务端信任列表
4. 自动刷新证书列表
5. 若服务端导入失败，显示警告但保留本地文件，可手动导入

### 部署管理

部署管理页面用于向机台分发配套工具软件、配置文件或其他辅助文件，覆盖以下操作：

1. 上传部署包与签名文件
2. 浏览部署包列表
3. 为指定机台创建部署任务
4. 查看机台部署历史
5. 对已安装记录发起卸载

### 本地打包器（部署管理页 + 离线工具页）

部署管理页中的"本地打包器"按钮（仅 Windows 桌面）和离线工具页"软件部署打包器"标签使用同一套打包逻辑：

- `software-deploy`
  - 适合带 `install.ps1` / `uninstall.ps1` 的配套软件安装包
  - 打包器会把脚本放在 ZIP 根目录，并生成带 `installScript`、`uninstallScript`、`requiresAdmin` 的 `deploy.json`
- `file-deploy`
  - 适合把文件直接解压到机台目标目录
  - 打包器会把文件负载放进 `payload/` 子目录，并把 `targetPath` 写入 `deploy.json`

### 使用建议

- 上传服务端前，优先用本地打包器生成 ZIP 和 `.zip.sig`
- 机台选择、日志、部署任务、部署历史都可以在同一个客户端中完成，无需切换旧 Web 管理页
- 移动端定位为纯远程管理：完全隐藏离线工具入口和部署页面的"本地打包器"按钮，但保留"上传部署包"等远程操作能力
- 管理端“导出原始文本”需要服务端连接和 OTP step-up，与机台使用 `NXLOG` 可移动盘复制本地 `vhdmounter.log` 是两条独立流程
