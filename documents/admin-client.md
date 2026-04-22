# 管理客户端安装

管理客户端基于 Flutter 构建，支持 Windows、Android 和 iOS。

---

## Windows 端

### 前置要求

- Flutter stable

### 安装步骤

```powershell
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d windows
```

---

## Android 端

```powershell
cd vhd_mount_admin_flutter
flutter pub get
flutter run -d android
```

> Android 模拟器访问本机服务时，使用 `http://10.0.2.2:8082`（若使用默认 Docker Compose 端口映射）。

---

## iOS 端

iOS 工程骨架已包含在仓库中，但构建和运行需要 macOS + Xcode。

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
