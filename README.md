# VHD Mounter - 智能SEGA街机游戏VHD挂载管理和运行保活工具

## ✨ 核心特性

### 🔍 智能扫描与识别
- **全盘扫描**：自动扫描所有固定磁盘驱动器的根目录
- **关键词过滤**：精确识别包含 `SDEZ`、`SDHD`、`SDDT` 关键词的VHD文件
- **快速定位**：优化的扫描算法，快速定位目标文件

### 💾 专业VHD管理
- **一键挂载**：自动将VHD文件挂载到M盘
- **智能清理**：挂载前自动分离现有VHD，避免冲突
- **安全卸载**：程序退出时自动解除VHD挂载，保护数据安全

### 🎯 用户友好界面
- **全屏选择**：多VHD文件时提供直观的全屏选择界面
- **实时状态**：操作过程中显示详细的状态信息
- **键盘操作**：支持方向键选择、回车确认、ESC退出

### 🔄 自动化运行
- **程序启动**：自动查找并启动package文件夹中的start.bat
- **进程监控**：持续监控 `sinmai`、`chusanapp`、`mu3` 相关进程
- **智能重启**：目标进程异常退出时自动重新启动

### 🛡️ 系统保护
- **关机监听**：监听Windows系统关机事件
- **安全退出**：确保在系统关机前完成VHD卸载操作
- **资源保护**：防止因异常退出导致的VHD资源占用

## 🚀 快速开始

### 系统要求

- **操作系统**：Windows 10 1903+ / Windows 11
- **运行时**：.NET 6.0 Runtime
- **权限**：管理员权限（VHD操作必需）
- **硬件**：至少1GB可用磁盘空间

### 安装与运行

#### 方式一：直接运行（推荐）
1. 下载最新版本的可执行文件
2. 右键点击 → "以管理员身份运行"
3. 程序将自动开始扫描和挂载流程

#### 方式二：从源码编译
```powershell
# 克隆仓库
git clone https://github.com/username/vhdmount.git
cd vhdmount

# 编译项目
dotnet build --configuration Release

# 发布独立应用
dotnet publish --configuration Release --self-contained true --runtime win-x64
```

## 📖 使用指南

### 基本操作流程

1. **启动程序**
   - 以管理员身份运行 `VHDMounter.exe`
   - 程序显示全屏白色界面并开始扫描

2. **VHD文件选择**
   - 单个文件：自动挂载
   - 多个文件：使用 ↑↓ 键选择，Enter 确认

3. **自动挂载与启动**
   - VHD文件挂载到M盘
   - 自动搜索package文件夹
   - 启动start.bat文件

4. **后台监控**
   - 程序最小化到系统托盘
   - 持续监控目标进程状态
   - 异常时自动重启应用

### 键盘快捷键

| 按键 | 功能 |
|------|------|
| ↑↓ | 在选择界面中切换选项 |
| Enter | 确认当前选择 |
| Esc | 安全退出程序 |

### 高级功能

#### 调试模式
程序内置调试功能，可以通过以下方式启用：
```powershell
# 启用详细日志输出
VHDMounter.exe --debug
```

#### 自定义配置
- **目标关键词**：可在源码中修改 `TARGET_KEYWORDS` 数组
- **监控进程**：可在源码中修改 `PROCESS_KEYWORDS` 数组
- **挂载盘符**：可在源码中修改 `TARGET_DRIVE` 常量

## 🔧 技术架构

### 核心技术栈
- **UI框架**：WPF (.NET 6.0)
- **VHD操作**：Windows DiskPart API
- **进程管理**：System.Diagnostics
- **系统集成**：Microsoft.Win32.SystemEvents
- **异步编程**：async/await 模式

### 关键组件

#### VHDManager 类
- `ScanForVHDFiles()` - VHD文件扫描
- `MountVHD()` - VHD挂载操作
- `UnmountVHD()` - VHD卸载操作
- `FindPackageFolder()` - Package文件夹定位
- `StartBatchFile()` - 批处理文件启动
- `IsTargetProcessRunning()` - 进程状态检查

#### MainWindow 类
- 用户界面管理
- 系统事件监听
- 应用程序生命周期控制

### 安全机制
- **权限检查**：启动时验证管理员权限
- **资源清理**：异常情况下的资源自动释放
- **进程隔离**：独立进程启动，避免相互影响
- **系统集成**：响应系统关机事件，确保数据安全

## 🐛 故障排除

### 常见问题

**Q: 程序启动后提示权限不足**
A: 确保以管理员身份运行程序，VHD挂载操作需要管理员权限。

**Q: 找不到符合条件的VHD文件**
A: 检查VHD文件名是否包含SDEZ、SDHD或SDDT关键词，且文件位于磁盘根目录。

**Q: VHD挂载失败**
A: 检查M盘是否被其他程序占用，程序会自动尝试清理但可能需要手动处理。

**Q: start.bat启动失败**
A: 确认package文件夹中存在start.bat文件，且文件具有执行权限。

**Q: 进程监控不工作**
A: 检查目标程序是否正常运行，进程名是否包含预定义的关键词。

### 日志和调试

程序运行时会输出详细的调试信息到控制台，可以通过以下方式查看：
1. 在命令行中启动程序
2. 查看控制台输出的调试信息
3. 根据错误信息进行相应的故障排除

## 🤝 贡献指南

我们欢迎社区贡献！请遵循以下步骤：

1. Fork 本仓库
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

### 开发环境设置
```powershell
# 安装依赖
dotnet restore

# 运行测试
dotnet test

# 启动调试
dotnet run --project VHDMounter.csproj
```

## 📄 许可证

本项目采用 MIT 许可证 - 查看 [LICENSE](LICENSE) 文件了解详情。

## 🙏 致谢

- 感谢 Microsoft 提供的 .NET 平台和 WPF 框架
- 感谢 Windows DiskPart 工具提供的VHD管理能力
- 感谢所有贡献者和用户的支持

---

**注意**：本工具仅供学习和研究使用，请确保遵守相关法律法规和软件许可协议。