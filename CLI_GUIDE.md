# Encrypted VHD Mount - CLI 调用指南

## 概述

`encrypted-vhd-mount` 是一个支持流式加解密的VHD处理程序，支持两种主要操作：
1. **初始化加密**：将明文VHD文件加密为EVHD格式
2. **挂载解密视图**：将加密的EVHD文件挂载到系统，通过虚拟盘符访问解密后的内容

本工具使用 AES-256-CTR 加密，并通过 PBKDF2 进行密钥派生，支持密码、密钥文件或两者结合的认证方式。

---

## 目录

- [基础用法](#基础用法)
- [命令详解](#命令详解)
  - [初始化加密](#初始化加密-init)
  - [挂载解密视图](#挂载解密视图)
  - [验证与测试](#验证与测试)
- [参数说明](#参数说明)
- [密码安全](#密码安全)
- [密钥派生方法](#密钥派生方法)
- [常见场景](#常见场景)
- [交互式脚本](#交互式脚本)
- [故障排除](#故障排除)

---

## 基础用法

### 可执行文件位置

在编译完成后，主要可执行文件位于：
```
build\encrypted-vhd-mount.exe      # 主程序
build\evhd-bench.exe               # 性能基准测试工具
build\evhd-selftest.exe            # 自测试工具
```

### 快速开始

```powershell
# 1. 加密 VHD 文件
"YourPassword" | .\build\encrypted-vhd-mount.exe /init plain.vhd /o encrypted.evhd --password-stdin

# 2. 挂载加密文件
"YourPassword" | .\build\encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\

# 3. 卸载
Dismount-VHD -Path "M:\decrypted.vhd"
```

---

## 命令详解

### 初始化加密 (/init)

将明文VHD文件加密为EVHD格式。

#### 基础语法
```powershell
encrypted-vhd-mount.exe /init <input-vhd> /o <output-evhd> [options]
```

#### 仅密码加密
```powershell
"MyPassword123" | encrypted-vhd-mount.exe /init c:\path\plain.vhd /o c:\path\encrypted.evhd --password-stdin
```

#### 仅密钥文件加密
使用 SHA256(keyfile) 作为密钥派生的密码：
```powershell
encrypted-vhd-mount.exe /init c:\path\plain.vhd /o c:\path\encrypted.evhd /k c:\path\keyfile.bin
```

#### 密码 + 密钥文件组合
两者结合提供双重认证：
```powershell
"MyPassword123" | encrypted-vhd-mount.exe /init c:\path\plain.vhd /o c:\path\encrypted.evhd --password-stdin /k c:\path\keyfile.bin
```

#### 参数说明
| 参数 | 说明 |
|-----|-----|
| `/init` | 初始化加密操作 |
| `<input-vhd>` | 明文VHD文件路径 |
| `/o` | 输出加密文件路径 |
| `--password-stdin` | 从 stdin 读取UTF-8密码（推荐） |
| `/k <keyfile>` | 可选的密钥文件路径 |

---

### 挂载解密视图

将加密的EVHD文件挂载为虚拟盘符，使系统能直接访问解密后的VHD内容。

#### 基础语法
```powershell
encrypted-vhd-mount.exe [options] <evhd-file> <mount-point>
```

#### 使用密码挂载
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\
```

#### 使用密钥文件挂载
```powershell
encrypted-vhd-mount.exe /k c:\path\keyfile.bin encrypted.evhd M:\
```

#### 密码 + 密钥文件组合
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin /k c:\path\keyfile.bin encrypted.evhd M:\
```

#### 启用 DirectIO 模式（提升性能）
禁用OS缓存，使用无缓冲I/O：
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin /direct encrypted.evhd M:\
```

#### 启用调试日志
显示 Dokan 调试信息：
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin /d encrypted.evhd M:\
```

#### 挂载后打开资源管理器
挂载成功后自动打开挂载点：
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin /l encrypted.evhd M:\
```

#### 组合使用多个选项
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin /direct /d /l encrypted.evhd M:\
```

#### 参数说明
| 参数 | 说明 |
|-----|-----|
| `--password-stdin` | 从 stdin 读取UTF-8密码 |
| `/k <keyfile>` | 密钥文件路径 |
| `/direct` | 启用 DirectIO 模式（提升性能） |
| `/d` | 启用 Dokan 调试日志 |
| `/l` | 挂载后打开资源管理器 |
| `<evhd-file>` | 加密VHD文件路径 |
| `<mount-point>` | 挂载点（盘符如 `M:\` 或文件夹路径） |

#### 不安全的密码参数（不推荐）
若需使用命令行参数传递密码，需显式启用：
```powershell
encrypted-vhd-mount.exe /p MyPassword123 --allow-insecure-password-arg encrypted.evhd M:\
```
**警告**：此方法在进程列表中暴露密码，仅用于兼容性场景。

#### 兼容旧版本容器
若容器缺少认证元数据（v1-v4版本），需显式启用：
```powershell
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin --allow-legacy-unauthenticated encrypted.evhd M:\
```

---

### 验证与测试

#### 性能基准测试
```powershell
# 运行基准测试（需要已挂载的加密容器）
.\build\evhd-bench.exe M:\decrypted.vhd
```

#### 自测试工具
```powershell
# 运行内置自测试
.\build\evhd-selftest.exe
```

---

## 参数说明

### 通用参数

| 参数 | 类型 | 说明 | 默认值 |
|-----|------|-----|--------|
| `--password-stdin` | 标志 | 从stdin读取UTF-8密码 | 关闭 |
| `/p` | 字符串 | 命令行密码（需 `--allow-insecure-password-arg`） | 无 |
| `/k` | 路径 | 密钥文件路径 | 无 |
| `/direct` | 标志 | 启用DirectIO（禁用OS缓存） | 关闭 |
| `/d` | 标志 | 启用Dokan调试日志 | 关闭 |
| `/l` | 标志 | 挂载后打开资源管理器 | 关闭 |
| `--allow-insecure-password-arg` | 标志 | 允许不安全的密码参数 | 关闭 |
| `--allow-legacy-unauthenticated` | 标志 | 允许旧版本容器 | 关闭 |

### 初始化专用参数

| 参数 | 说明 |
|-----|-----|
| `/init` | 初始化加密操作 |
| `/o` | 输出文件路径 |

---

## 密码安全

### 推荐做法 ✅

**使用 `--password-stdin` 从管道传递密码：**

```powershell
# 方式1：管道处理
"MyPassword123" | encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\

# 方式2：读取密码文件
Get-Content .\password.txt | encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\

# 方式3：从SecureString转换（PowerShell）
$securePass = Read-Host -AsSecureString
$plainPass = [System.Net.NetworkCredential]::new('', $securePass).Password
$plainPass | encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\
```

### 不推荐做法 ❌

**避免在命令行参数中暴露密码：**

```powershell
# 危险：密码显示在进程列表中
encrypted-vhd-mount.exe /p "MyPassword123" encrypted.evhd M:\
```

若必须使用此方式，需额外启用标志：
```powershell
encrypted-vhd-mount.exe /p "MyPassword123" --allow-insecure-password-arg encrypted.evhd M:\
```

---

## 密钥派生方法

工具支持三种密钥派生方法（KDF），可根据输入自动选择：

### 1. KDF_PASSWORD
**仅使用密码派生**
```
PBKDF2-HMAC-SHA256(password, salt, iterations=200000) → 32字节密钥
```

使用：
```powershell
"MyPassword" | encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\
```

### 2. KDF_KEYFILE
**仅使用密钥文件派生**
```
SHA256(keyfile) → 用作PBKDF2的"密码"输入
```

使用：
```powershell
encrypted-vhd-mount.exe /k keyfile.bin encrypted.evhd M:\
```

### 3. KDF_PASSWORD_PLUS_KEYFILE
**密码和密钥文件组合派生**
```
pbkdfKey = PBKDF2-HMAC-SHA256(password, salt, iterations)
finalKey = HMAC-SHA256(key=SHA256(keyfile), data=pbkdfKey||salt||nonce)
```

使用：
```powershell
"MyPassword" | encrypted-vhd-mount.exe --password-stdin /k keyfile.bin encrypted.evhd M:\
```

---

## 常见场景

### 场景1：创建并挂载加密VHD

```powershell
# 步骤1：加密存在的VHD
"SecurePassword2024" | .\build\encrypted-vhd-mount.exe /init C:\VMs\disk.vhd /o C:\Secure\disk.evhd --password-stdin

# 步骤2：挂载到系统
"SecurePassword2024" | .\build\encrypted-vhd-mount.exe --password-stdin C:\Secure\disk.evhd M:\

# 步骤3：在PowerShell中挂载VHD到虚拟磁盘
Mount-VHD -Path "M:\decrypted.vhd" -ReadOnly:$false

# 步骤4：使用虚拟磁盘...
Get-Volume

# 步骤5：清理
Dismount-VHD -Path "M:\decrypted.vhd"
```

### 场景2：使用密钥文件增强安全性

```powershell
# 步骤1：创建密钥文件（示例）
$keyBytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
[System.IO.File]::WriteAllBytes("C:\Secure\keyfile.bin", $keyBytes)

# 步骤2：加密VHD时同时使用密码和密钥文件
"MyPassword" | .\build\encrypted-vhd-mount.exe /init C:\VMs\disk.vhd /o C:\Secure\disk.evhd --password-stdin /k C:\Secure\keyfile.bin

# 步骤3：挂载时需同时提供两者
"MyPassword" | .\build\encrypted-vhd-mount.exe --password-stdin /k C:\Secure\keyfile.bin C:\Secure\disk.evhd M:\
```

### 场景3：性能优化挂载

```powershell
# 使用DirectIO模式禁用OS缓存
"MyPassword" | .\build\encrypted-vhd-mount.exe --password-stdin /direct C:\Secure\disk.evhd M:\
```

### 场景4：调试问题

```powershell
# 启用详细日志进行故障排除
"MyPassword" | .\build\encrypted-vhd-mount.exe --password-stdin /d C:\Secure\disk.evhd M:\
```

---

## 交互式脚本

项目提供了交互式 PowerShell 脚本 `evhd-interactive.ps1`，简化了创建和挂载流程。

### 基本用法

```powershell
# 使用默认位置的可执行文件
powershell -ExecutionPolicy Bypass -File .\evhd-interactive.ps1

# 指定可执行文件路径
powershell -ExecutionPolicy Bypass -File .\evhd-interactive.ps1 -ExecutablePath .\build\encrypted-vhd-mount.exe
```

### 交互流程

脚本会依次询问：

1. **操作选择**：创建加密容器 或 挂载容器
2. **文件路径**：输入源文件或加密文件路径
3. **输出路径**（仅创建时）：输出EVHD文件路径
4. **挂载点**（仅挂载时）：虚拟盘符或文件夹路径
5. **调试选项**：是否启用Dokan调试日志
6. **密钥文件**：可选的密钥文件路径
7. **密码输入**：安全输入密码

### 脚本特性

- ✅ 密码通过 `Read-Host -AsSecureString` 安全读取
- ✅ 不会在命令行参数中暴露密码
- ✅ 自动寻找可执行文件（支持多个位置）
- ✅ 完全交互式，无需手工拼接命令

---

## 故障排除

### 问题1：找不到可执行文件

**错误信息**：
```
Cannot find encrypted-vhd-mount.exe
```

**解决方案**：
```powershell
# 确保项目已编译
cd encrypted-vhd-mount\build
cmake .. -G "Visual Studio 16 2019"
cmake --build . --config Release

# 返回根目录
cd ..\..

# 使用完整路径运行
.\build\encrypted-vhd-mount.exe --password-stdin encrypted.evhd M:\
```

### 问题2：挂载点不可用

**错误信息**：
```
Mount point is busy or invalid
```

**解决方案**：
```powershell
# 检查挂载点是否已被占用
Get-Volume | Where-Object {$_.DriveLetter -eq 'M'}

# 卸载任何已有的挂载
Dismount-VHD -Path "M:\decrypted.vhd" -Confirm:$false

# 清除Dokan挂载
dokan-control.exe /u M
```

### 问题3：密码/密钥文件错误

**错误信息**：
```
Authentication failed
```

**解决方案**：
1. 确认密码正确（注意空格和大小写）
2. 确认密钥文件未损坏
3. 检查EVHD容器是否使用了多因素认证：
   ```powershell
   # 如果创建时使用了 /k keyfile，挂载时也必须使用
   "MyPassword" | encrypted-vhd-mount.exe --password-stdin /k keyfile.bin encrypted.evhd M:\
   ```

### 问题4：旧版本容器兼容性

**错误信息**：
```
Legacy container without authentication metadata
```

**解决方案**：
```powershell
# 添加 --allow-legacy-unauthenticated 标志
"MyPassword" | .\build\encrypted-vhd-mount.exe --password-stdin --allow-legacy-unauthenticated encrypted.evhd M:\
```

### 问题5：性能问题

**现象**：读写速度缓慢

**解决方案**：
```powershell
# 尝试启用 DirectIO 模式
"MyPassword" | encrypted-vhd-mount.exe --password-stdin /direct encrypted.evhd M:\

# 检查Dokan日志（启用调试）
"MyPassword" | encrypted-vhd-mount.exe --password-stdin /d encrypted.evhd M:\

# 运行基准测试对比
.\build\evhd-bench.exe M:\decrypted.vhd
```

---

## 进阶配置

### 容器格式版本

EVHD 容器支持多个版本，文件头包含以下字段：

- **v1/v2/v3 公共字段**：
  - `magic` = "EVHD0001"
  - `version`：容器版本号
  - `kdfIterations`：PBKDF2迭代次数（默认 200000）
  - `originalSize`：明文VHD大小
  - `salt[16]`：PBKDF2盐值
  - `nonce[16]`：AES-CTR初始向量基

- **v2 扩展**：原始文件名元数据
- **v3 扩展**：KDF方法记录
- **v4 扩展**：认证机制

### 环境变量配置

当前版本主要通过命令行参数配置，但可通过PowerShell脚本变量设置：

```powershell
# 定义默认密钥文件位置
$DefaultKeyFile = "C:\Secure\keyfile.bin"

# 使用变量
"MyPassword" | .\build\encrypted-vhd-mount.exe --password-stdin /k $DefaultKeyFile encrypted.evhd M:\
```

---

## 安全最佳实践

1. **密码强度**：使用至少 16 个字符的强密码
2. **密钥管理**：将密钥文件与EVHD文件分离存储
3. **访问控制**：限制EVHD文件的文件系统权限
4. **审计logging**：启用 `/d` 调试模式进行操作审计
5. **备份**：定期备份EVHD文件和密钥文件（分开存储）

---

## 相关资源

- [项目README](./encrypted-vhd-mount/README.md)
- [交互式脚本](./evhd-interactive.ps1)
- 许可证：见项目顶级目录

---

## Version

- **Last Updated**：2026年4月
- **Tool Version**：Latest（refer to build output）
