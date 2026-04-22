---
layout: default
title: 管理客户端安装
---

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

- **LiveContainer**：在已越狱或使用 TrollStore 的设备上，通过 LiveContainer 直接运行未签名的 IPA
- **自行签名**：使用个人 Apple Developer 账号或 AltStore、SideStore 等工具对 IPA 进行自签名后安装

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
