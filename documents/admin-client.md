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
- 管理员登录与 OTP 验证
- 机台管理与注册证书配置
- 审计日志查看
- 安全设置（密码修改、OTP 轮换）
- 部署包上传、部署任务下发、机台部署历史与卸载
- 本地打包器：直接在管理员电脑上生成 `software-deploy` / `file-deploy` ZIP 与签名

## 新功能速览

### 部署管理

管理客户端现在提供“部署管理”页面，覆盖以下操作：

1. 上传部署包与签名文件
2. 浏览部署包列表
3. 为指定机台创建部署任务
4. 查看机台部署历史
5. 对已安装记录发起卸载

### 本地打包器

“部署管理”页中的“本地打包器”按钮可直接生成服务端可接受的部署包：

- `software-deploy`
  - 适合带 `install.ps1` / `uninstall.ps1` 的配套软件安装包
  - 打包器会把脚本放在 ZIP 根目录，并生成带 `installScript`、`uninstallScript`、`requiresAdmin` 的 `deploy.json`
- `file-deploy`
  - 适合把文件直接解压到机台目标目录
  - 打包器会把文件负载放进 `payload/` 子目录，并把 `targetPath` 写入 `deploy.json`

### 使用建议

- 上传服务端前，优先用本地打包器生成 ZIP 和 `.zip.sig`
- 机台选择、日志、部署任务、部署历史都可以在同一个客户端中完成，无需切换旧 Web 管理页
