# Windows 客户端安装

---

## 前置要求

- Windows 10 / Windows 11
- 管理员权限（用于 VHD 挂载）

---

## 安装方式一：下载预构建版本

从 GitHub Releases 下载对应版本的 ZIP 包，解压后运行：

```powershell
# 基础版
VHDMounter.exe

# 增强版（支持 Maimoller HID 系统菜单）
VHDMounter_Maimoller.exe
```

> 必须以管理员身份运行。

---

## 安装方式二：从源码构建

### 前置要求

- .NET 8 SDK

### 构建步骤

```powershell
scripts\build.bat
```

构建产物位于 `single/` 目录：

| 文件 | 说明 |
|------|------|
| `VHDMounter.exe` | 基础版客户端 |
| `VHDMounter_Maimoller.exe` | 增强版客户端（HID 菜单） |
| `Updater.exe` | 离线更新工具 |
| `VHDMountAdminTools.exe` | 离线管理工具 |
| `vhdmonter_config.ini` | 客户端配置模板 |

---

## 客户端配置

编辑 `vhdmonter_config.ini`：

```ini
[Settings]
ServerBaseUrl=http://127.0.0.1:8080
EnableRemoteSelection=false
RegistrationCertificatePath=
RegistrationCertificatePassword=
MachineId=MACHINE_001
EnableProtectionCheck=false
EnableLogUpload=false
```

| 配置项 | 说明 |
|--------|------|
| `ServerBaseUrl` | 服务端地址 |
| `MachineId` | 机台唯一标识 |
| `EnableLogUpload` | 是否启用机台日志上报 |

---

## Maimoller HID 系统菜单（仅限增强版）

使用 `VHDMounter_Maimoller.exe` 时，可通过 Maimoller 操控面板访问系统菜单：

| 操作 | 按键 |
|------|------|
| 打开菜单 | Coin 长按 15 秒 或 键盘 F12 |
| 上/下切换 | 6 号键 / 3 号键 |
| 确认 | 4 号键 |
| 返回/关闭 | 5 号键 |

菜单功能包括：系统重启、关机、系统信息查看、网络设置、音频设置。
