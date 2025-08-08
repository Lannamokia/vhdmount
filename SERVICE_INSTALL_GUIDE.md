# VHD Mounter Windows服务安装指南

## 概述

VHD Mounter现在支持作为Windows服务运行，具有管理员权限，可以实现用户登录时自动启动功能。

## 安装方式

### 方法一：使用批处理文件（推荐）

1. 右键点击 `install_service.bat` 文件
2. 选择"以管理员身份运行"
3. 按照提示选择操作：
   - 选择 `1` 安装服务
   - 选择 `2` 卸载服务
   - 选择 `3` 启动服务
   - 选择 `4` 停止服务
   - 选择 `5` 查看服务状态

### 方法二：手动命令行安装

以管理员权限打开命令提示符，执行以下命令：

```cmd
# 安装服务（用户登录时启动）
sc create VHDMounterService binPath= "C:\Path\To\VHDMounter.exe --service" start= demand DisplayName= "VHD Mounter Service"

# 设置服务描述
sc description VHDMounterService "VHD文件自动挂载服务（用户登录时启动）"

# 设置失败恢复选项
sc failure VHDMounterService reset= 86400 actions= restart/60000/restart/60000/restart/60000

# 添加用户登录时启动服务的注册表项
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "VHDMounterServiceStarter" /t REG_SZ /d "cmd /c sc start VHDMounterService" /f

# 启动服务
sc start VHDMounterService
```

## 卸载服务

```cmd
# 停止服务
sc stop VHDMounterService

# 删除服务
sc delete VHDMounterService

# 删除用户登录启动项
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "VHDMounterServiceStarter" /f
```

## 服务特性

- **管理员权限**：服务以LocalSystem账户运行，具有完整的系统权限
- **用户登录启动**：用户登录时自动启动服务，无需手动操作
- **延迟启动**：服务启动后延迟10秒再执行主要功能
- **自动恢复**：服务异常退出时会自动重启
- **事件日志**：所有操作都会记录到Windows事件日志中

## 运行模式

程序支持两种运行模式：

1. **WPF应用程序模式**：直接运行 `VHDMounter.exe`
2. **Windows服务模式**：通过 `VHDMounter.exe --service` 或安装为系统服务

## 注意事项

1. 安装和卸载服务需要管理员权限
2. 服务模式下程序没有用户界面，所有状态通过事件日志查看
3. 如果同时运行应用程序和服务，可能会产生冲突
4. 建议使用服务模式实现开机自启，应用程序模式用于调试和手动操作

## 故障排除

### 查看服务状态
```cmd
sc query VHDMounterService
```

### 查看事件日志
1. 打开"事件查看器"
2. 导航到"Windows日志" > "应用程序"
3. 查找来源为"VHDMounterService"的事件

### 常见问题

- **服务安装失败**：确保以管理员权限运行安装命令
- **服务启动失败**：检查程序文件路径是否正确
- **VHD挂载失败**：确保VHD文件存在且可访问
- **权限不足**：服务以LocalSystem运行，应该有足够权限

## 技术细节

- 服务名称：`VHDMounterService`
- 显示名称：`VHD Mounter Service`
- 启动类型：手动（通过用户登录时的注册表项触发）
- 账户：LocalSystem
- 恢复策略：失败时自动重启（最多3次）
- 登录启动项：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\VHDMounterServiceStarter`