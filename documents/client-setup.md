---
layout: default
title: Windows 客户端安装
---

# Windows 客户端安装

{% include docs-sidebar.html page_key="client-setup" %}

---

## 前置要求

- Windows 10 / Windows 11
- 管理员权限（用于 VHD 挂载）

## 磁盘空间准备

初始化系统时，建议预留一个 **D 盘**用于存放游戏内容的 VHD 虚拟磁盘映像或 EVHD 加密映像容器：

| 游戏 | 建议预留空间 |
|------|------------|
| Maimai | 100 GB |
| Chunithm / Ongeki | 40 GB |

客户端启动后会自动扫描 D 盘根目录中的 `.vhd` / `.evhd` 文件并挂载到 `M:\`。

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

使用 `VHDMounter_Maimoller.exe` 时，可通过 Maimoller 操控面板访问系统菜单。

简要操作：

| 操作 | 按键 |
|------|------|
| 打开菜单 | Coin 长按 15 秒 |
| 上/下切换 | 6 号键 / 3 号键 |
| 确认 | 4 号键 |
| 返回/关闭 | 5 号键 |

完整功能说明、HID 数据包格式、数字编辑模式、故障排查等详情请参阅 [Maimoller HID 系统菜单指南](maimoller)。

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

---

## 生产环境机台加固（Windows IoT Enterprise）

建议在正式机台上部署 **Windows IoT Enterprise**，利用系统级锁定功能进一步减少暴露面。

### 限制键盘输入

通过 KeyboardFilter 禁用非必要按键：

```powershell
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter" -Name "Config" -Value "..."
```

配置值的完整语法和可用键码请参阅微软官方文档：<https://learn.microsoft.com/zh-cn/windows/configuration/keyboard-filter/>

恢复：删除或修改对应注册表值。

### 自动登录

配置 AppUser 自动登录：

```powershell
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "AutoAdminLogon" -Value "1"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultUserName" -Value "AppUser"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name "DefaultPassword" -Value "密码"
```

### 隐藏自动登录页面

```powershell
Set-ItemProperty "HKLM:\Software\Microsoft\Windows Embedded\EmbeddedLogon" -Name "HideAutoLogonUI" -Value 1
```

恢复：将值改为 `0`。

### 隐藏登录页面右下角元素

```powershell
Set-ItemProperty "HKLM:\Software\Microsoft\Windows Embedded\EmbeddedLogon" -Name "BrandingNeutral" -Value 0x3f
```

恢复：将值改回 `0`。

### 统一写入过滤器（UWF）

UWF 可将系统盘设为只读，重启后还原。若需临时修改系统配置，先禁用过滤再重启：

```powershell
# 禁用 UWF
uwfmgr filter disable
Restart-Computer

# 修改完成后重新启用
uwfmgr filter enable
Restart-Computer
```

### 登录页面不显示上次用户名

```powershell
# 启用"不显示上次登录的用户名"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name "dontdisplaylastusername" -Value 1
```

恢复：改为 `0`，或通过 `secpol.msc` > 本地策略 > 安全选项 > "交互式登录: 不显示上次登录的用户名" 设置为"已禁用"。

### 系统盘 BitLocker 加密

建议为系统分区启用 BitLocker，防止物理拆盘导致的配置泄露：

```powershell
# 检查 BitLocker 状态
Get-BitLockerVolume C:

# 启用 BitLocker（需要 TPM 或恢复密钥）
Enable-BitLocker -MountPoint C: -RecoveryPasswordProtector

# 保存恢复密钥到安全位置
(Get-BitLockerVolume C:).KeyProtector | Where-Object { $_.KeyProtectorType -eq "RecoveryPassword" }
```

> 启用 BitLocker 后首次启动需要完成加密过程，建议在机台正式投用前完成。

### 检查清单

| 加固项 | 推荐值 | 恢复方式 |
|--------|--------|----------|
| KeyboardFilter | 按需配置 | 修改注册表 |
| AutoAdminLogon | `1` | 改为 `0` |
| HideAutoLogonUI | `1` | 改为 `0` |
| BrandingNeutral | `0x3f` (63) | 改为 `0` |
| UWF | 按需启用/禁用 | `uwfmgr filter enable/disable` |
| dontdisplaylastusername | `1` | 改为 `0` |
| BitLocker | 启用 | `Disable-BitLocker C:` |

{% include docs-sidebar-end.html %}
