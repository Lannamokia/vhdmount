# Windows 客户端安装

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

UWF（Unified Write Filter）可将指定卷设为只读，所有写入会落到覆盖层中，重启后自动丢弃。对于机台环境，推荐把**系统盘**做成受保护卷，再把必须持久化的目录显式加入例外。

> 下面示例假设：系统盘为 `C:`，游戏数据或 VHD 文件放在 `D:`，挂载目标盘符为 `M:`，远程部署根目录为 `C:\SOFT`。

#### 建议保护策略

| 卷 / 路径 | 建议 | 说明 |
|----------|------|------|
| `C:` | 保护 | 系统盘通常应为只读，降低配置漂移和误操作风险 |
| `D:` | 视场景决定 | 如果 `D:` 承载 VHD/EVHD 文件且需要保留更新结果，通常不要保护 |
| `M:` | 不配置为受保护卷 | `M:` 是运行期挂载盘符，不应直接作为 UWF 配置目标 |
| `C:\SOFT` | 加目录例外 | 远程部署 `software-deploy` 的稳定安装根目录 |
| `C:\VHD` 或 `C:\Users\Test\Desktop` 这类程序部署目录 | 加目录例外 | 如果客户端、Updater、配置文件放在这里，建议放行 |
| `vhdmonter_config.ini` 所在目录 | 加目录或文件例外 | 保证机台 ID、证书路径、服务器地址等配置可持久化 |
| `machine-log-spool.jsonl` / `machine-log-client.log` / `vhdmounter.log` | 按需放行 | 若希望日志跨重启保留，应加入文件例外 |

#### 1. 启用 UWF 并保护卷

以管理员身份运行 PowerShell：

```powershell
# 启用 UWF 功能（首次配置时执行一次）
dism /online /Enable-Feature /FeatureName:Client-UnifiedWriteFilter /All

# 把系统盘加入受保护卷
uwfmgr volume protect C:

# 如需取消保护
# uwfmgr volume unprotect C:

# 启用 UWF 过滤器
uwfmgr filter enable
Restart-Computer
```

#### 2. 为常见持久化路径添加目录例外

```powershell
# software-deploy 远程部署稳定安装根目录
uwfmgr file add-exclusion C:\SOFT

# 客户端程序目录（按你的实际部署路径修改）
uwfmgr file add-exclusion C:\VHD

# 如果你把程序直接放在桌面或自定义目录，也应加例外
# uwfmgr file add-exclusion C:\Users\Test\Desktop
```

如果客户端和配置文件不在同一路径，建议把配置目录单独放行：

```powershell
# 允许客户端配置持久化
uwfmgr file add-exclusion C:\VHD\vhdmonter_config.ini
```

#### 3. 为常见日志 / spool 文件添加文件例外

如果你希望这些文件在重启后仍然保留，可显式加入文件例外：

```powershell
uwfmgr file add-exclusion C:\VHD\vhdmounter.log
uwfmgr file add-exclusion C:\VHD\machine-log-spool.jsonl
uwfmgr file add-exclusion C:\VHD\machine-log-client.log
```

如果日志目录和程序目录不同，请替换为你的实际路径。

#### 4. 查看当前保护状态和例外

```powershell
# 查看 UWF 当前配置
uwfmgr get-config

# 查看受保护卷
uwfmgr volume get-config

# 查看文件 / 目录例外
uwfmgr file get-exclusions
```

#### 5. 维护流程

若需临时修改系统配置，先禁用过滤再重启：

```powershell
# 禁用 UWF
uwfmgr filter disable
Restart-Computer

# 修改完成后重新启用
uwfmgr filter enable
Restart-Computer
```

#### 6. 常见建议

- **VHD / EVHD 数据文件所在盘**：如果需要保留资源更新结果，通常不要把该数据盘做成受保护卷
- **远程部署目录 `C:\SOFT`**：如果你启用了 `software-deploy`，这个目录几乎总是应该加入例外
- **客户端主程序目录**：如果 Updater 需要直接替换程序文件，这个目录也应该加入例外
- **配置文件目录**：机台 ID、注册证书路径、服务端地址等配置建议持久化，不要让每次重启都回滚
- **日志文件**：正式机房如果只关心运行期日志、重启后可丢弃，可以不加例外；若要跨重启排障，则应明确放行

### 登录页面不显示上次用户名

```powershell
# 启用"不显示上次登录的用户名"
Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name "dontdisplaylastusername" -Value 1
```

恢复：改为 `0`，或通过 `secpol.msc` > 本地策略 > 安全选项 > "交互式登录: 不显示上次登录的用户名" 设置为"已禁用"。

### 系统盘 BitLocker 加密

建议为系统分区启用 BitLocker，防止物理拆盘导致的配置泄露：

```powershell
# 定义固定的恢复密码
$fixedPassword = "123456-234567-345678-456789-567890-678901-789012-890123"

# 启用 BitLocker（TPM 保护器）
Enable-BitLocker -MountPoint "C:" -EncryptionMethod XtsAes256 -TpmProtector -SkipHardwareTest

# 移除旧的恢复密码保护器
$keyProtectors = (Get-BitLockerVolume -MountPoint "C:").KeyProtector |
    Where-Object { $_.KeyProtectorType -eq "RecoveryPassword" }
foreach ($kp in $keyProtectors) {
    Remove-BitLockerKeyProtector -MountPoint "C:" -KeyProtectorId $kp.KeyProtectorId
}

# 添加固定恢复密码
Add-BitLockerKeyProtector -MountPoint "C:" -RecoveryPasswordProtector -RecoveryPassword $fixedPassword
```

### 恢复密钥规则

BitLocker 的恢复密钥（Recovery Password）是一个 **48 位数字**，格式固定为：

- 8 组
- 每组 6 位数字
- 组与组之间用 `-` 分隔
- 每组数值必须 **小于 `720896`**
- 每组数值必须能被 **11** 整除

这意味着恢复密钥并不是任意 6 位数字的简单拼接。只要每组都满足这两个数值约束，就可以手工指定固定值，并统一部署到多台设备。

> 统一固定恢复密钥在批量部署上更省事，但也意味着一旦该值泄露，所有使用该值的设备都会一起失去恢复口令隔离。正式环境请按你的风控要求决定是否接受这一取舍。

### 恢复密钥生成器

<RecoveryPasswordGenerator />

> 该生成器为**纯前端本地生成**：不会把任何恢复密钥上传、保存或同步到服务端，也不会写入浏览器外的持久存储。生成后请立即复制并按你的运维流程自行保存。

### 部署建议

1. 先启用 TPM 保护器
2. 清理掉系统自动生成的旧恢复密码保护器
3. 再写入符合规则的固定恢复密码
4. 记录最终写入值，并在机房资产台账中保存

### 校验当前恢复密码

```powershell
Get-BitLockerVolume -MountPoint "C:" | Select-Object -ExpandProperty KeyProtector
```

重点查看：

- `KeyProtectorType` 是否包含 `RecoveryPassword`
- `RecoveryPassword` 是否就是你计划部署的固定值

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
