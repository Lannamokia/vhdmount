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
| 打开菜单 | Coin 长按 15 秒 |
| 上/下切换 | 6 号键 / 3 号键 |
| 确认 | 4 号键 |
| 返回/关闭 | 5 号键 |

菜单功能包括：系统重启、关机、系统信息查看、网络设置、音频设置。

---

## Shell Launcher 配置（ kiosk 模式）

为防止机台桌面泄露和非法操作，建议将 Windows 默认 Shell 替换为 VHDMounter。

### 前置要求

- Windows 10/11 Pro 或 Enterprise
- 已部署 VHDMounter.exe 到固定路径（如 `C:\VHD\VHDMounter.exe`）

### 配置步骤

**1. 备份原 Shell 配置**

以管理员身份运行 PowerShell：

```powershell
# 查看当前 Shell
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell
```

**2. 替换默认 Shell**

```powershell
# 将 Explorer 替换为 VHDMounter（请根据实际路径修改）
$vhdPath = "C:\VHD\VHDMounter.exe"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell -Value $vhdPath
```

**3. 恢复 Explorer（如需回退）**

```powershell
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell -Value "explorer.exe"
```

**4. 重启生效**

```powershell
Restart-Computer
```

### 注意事项

| 场景 | 说明 |
|------|------|
| 首次配置 | 建议先手动运行确认 VHDMounter 能正常工作，再替换 Shell |
| 调试维护 | 可临时恢复 `explorer.exe`，完成后再切回 VHDMounter |
| 自动登录 | 可配合 `AutoAdminLogon` 实现开机直接进入机台界面，无需手动登录 |
| 任务管理器 | 如需保留紧急维护入口，可通过组策略限制任务管理器权限而非完全禁用 |

### 进阶：配合自动登录

如需实现开机自动进入机台界面：

```powershell
# 设置自动登录（请替换为实际用户名和密码）
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name AutoAdminLogon -Value "1"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name DefaultUserName -Value "机台用户名"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name DefaultPassword -Value "机台密码"
```

> 密码以明文存储在注册表中，仅在受控物理环境下使用。
