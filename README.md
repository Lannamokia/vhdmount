# VHD Mounter - 智能SEGA街机游戏VHD挂载管理和运行保活工具

## ✨ 核心特性

### 🔍 智能扫描与识别
- **全盘扫描**：自动扫描所有固定磁盘驱动器的根目录
- **关键词过滤**：精确识别包含 `SDEZ`、`SDHD`、`SDDT` 关键词的VHD文件
- **快速定位**：优化的扫描算法，快速定位目标文件

### 🌐 智能网络配置管理
- **远程关键词获取**：通过HTTP API自动获取启动关键词，实现集中化配置管理
- **配置文件驱动**：通过`vhdmonter_config.ini`灵活控制远程功能开关和服务器地址
- **智能降级机制**：远程获取失败时自动回退到本地选择模式，确保程序稳定运行
- **跨平台服务器支持**：配套VHDSelectServer提供Web GUI和API接口，支持Docker部署

### 🔌 智能USB设备管理
- **NX_INS设备识别**：自动检测卷标为"NX_INS"的USB设备，专为街机系统优化
- **USB优先策略**：优先使用USB设备中的VHD文件，确保使用最新版本
- **智能文件替换**：根据关键词匹配，自动用USB中的VHD文件替换本地对应文件
- **双重扫描机制**：同时扫描USB和本地VHD文件，提供完整的文件管理方案
- **状态实时反馈**：详细显示USB设备检测、文件扫描和替换过程的每个步骤

### 💾 专业VHD管理
- **一键挂载**：自动将VHD文件挂载到M盘
- **智能清理**：挂载前自动分离现有VHD，避免冲突
- **安全卸载**：程序退出时自动解除VHD挂载，保护数据安全

### 🎯 用户友好界面
- **全屏选择**：多VHD文件时提供直观的全屏选择界面
- **实时状态**：操作过程中显示详细的状态信息
- **步骤暂停**：每个主要执行步骤完成后暂停2秒显示结果，便于用户查看执行状态
- **键盘操作**：支持方向键选择、回车确认、ESC退出

### 🔄 自动化运行
- **程序启动**：根据文件名关键词自动选择启动目录（SDHD → bin，其余 → package），并执行 start.bat（目录名忽略大小写）
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
   - 程序显示全屏白色界面并开始10秒延迟启动
   - 延迟完成后开始主流程扫描

2. **智能扫描过程**
   - **远程VHD选择检查**：
     * 读取`vhdmonter_config.ini`配置文件，检查`EnableRemoteSelection`开关状态
     * 如启用远程功能，向配置的`BootImageSelectUrl`发送HTTP GET请求
     * 解析JSON响应中的`BootImageSelected`字段获取目标关键词
     * 显示远程获取状态和选择的关键词信息
     * 执行完成后暂停2秒显示检查结果
   - **专业USB设备识别**：
     * 自动检测所有可移动设备，精确识别卷标为"NX_INS"的专用USB设备
     * 显示USB设备详细信息（设备名称、路径等）
     * 执行完成后暂停2秒显示检测状态
   - **USB优先文件扫描**：
     * 在检测到的NX_INS USB设备中扫描所有VHD文件
     * 支持根目录下的所有.vhd格式文件识别
     * 实时显示扫描进度和发现的文件数量
     * 扫描完成后暂停2秒显示结果统计
   - **本地VHD文件扫描**：扫描本地VHD文件并显示数量（暂停2秒）
   - **智能文件替换处理**：
     * 基于关键词匹配算法，将USB中的VHD文件与本地文件进行智能配对
     * 自动执行文件替换操作，确保使用最新版本的游戏文件
     * 详细显示每个替换操作的源文件和目标文件信息
     * 替换完成后暂停2秒显示操作结果

3. **VHD文件选择**
   - **远程优先策略**：
     * 如远程获取到有效关键词，自动在本地VHD文件中查找匹配项
     * 基于关键词匹配算法（SDEZ、SDHD、SDDT）进行精确匹配
     * 找到匹配文件时直接启动，无需用户手动选择
     * 未找到匹配时显示提示信息并回退到手动选择模式
   - **本地选择模式**：
     * 单个文件：自动挂载启动
     * 多个文件：提供全屏选择界面，支持 ↑↓ 键选择，Enter 确认

4. **自动挂载与启动**
   - **VHD挂载**：将选定的VHD文件挂载到M盘（暂停2秒显示结果）
   - **目录搜索**：自动搜索启动目录（SDHD → bin，其余 → package，忽略大小写）（暂停2秒显示路径）
   - **程序启动**：在目标目录执行 start.bat 文件（暂停2秒显示结果）

5. **后台监控**
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
- **远程配置管理**：
  * `EnableRemoteSelection=true/false` - 启用/禁用远程VHD选择功能
  * `BootImageSelectUrl=http://server:port/api/boot-image-select` - 远程服务器API地址
  * 配置文件：`vhdmonter_config.ini`（与程序同目录）
- **本地代码配置**：
  * **目标关键词**：可在源码中修改 `TARGET_KEYWORDS` 数组
  * **监控进程**：可在源码中修改 `PROCESS_KEYWORDS` 数组
  * **挂载盘符**：可在源码中修改 `TARGET_DRIVE` 常量

## 🔧 技术架构

### 核心技术栈
- **UI框架**：WPF (.NET 6.0)
- **VHD操作**：Windows DiskPart API
- **进程管理**：System.Diagnostics
- **系统集成**：Microsoft.Win32.SystemEvents
- **异步编程**：async/await 模式

### 关键组件

#### VHDManager 类
- `GetRemoteVHDSelection()` - 远程VHD关键词获取与解析
- `FindVHDByKeyword()` - 基于关键词的VHD文件匹配查找
- `ReadConfig()` - 配置文件读取与解析
- `ScanForVHDFiles()` - 本地VHD文件扫描
- `FindNXInsUSBDrive()` - NX_INS专用USB设备检测与识别
- `ScanUSBForVHDFiles()` - USB设备VHD文件扫描
- `ReplaceLocalVHDFiles()` - 智能文件替换（USB优先策略）
- `MountVHD()` - VHD挂载操作
- `UnmountVHD()` - VHD卸载操作
- `FindFolder(folderName)` - 通用目录定位（忽略大小写）
- `FindPackageFolder()` - Package文件夹定位（对 `FindFolder("package")` 的封装）
- `StartBatchFile()` - 批处理文件启动
- `IsTargetProcessRunning()` - 进程状态检查

#### MainWindow 类
- 用户界面管理
- 系统事件监听
- 应用程序生命周期控制
- 执行步骤状态显示和暂停控制

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
A: 确认对应启动目录中存在 start.bat 文件（SDHD: bin；其他关键词: package），且目录名大小写不影响搜索和定位。

**Q: 进程监控不工作**
A: 检查目标程序是否正常运行，进程名是否包含预定义的关键词。

**Q: 程序执行过程中暂停是否正常**
A: 程序在每个主要步骤完成后会自动暂停2秒显示执行结果，这是正常行为。启动时的10秒延迟不受此影响。如需跳过暂停，请等待自动继续。

**Q: 检测不到NX_INS USB设备**
A: 确认USB设备已正确连接且卷标设置为"NX_INS"。可在资源管理器中查看USB设备属性确认卷标。

**Q: USB设备中的VHD文件无法被识别**
A: 确保VHD文件位于USB设备根目录，且文件扩展名为.vhd。程序只扫描根目录下的VHD文件。

**Q: USB文件替换本地文件失败**
A: 检查本地VHD文件是否被其他程序占用，确保有足够的磁盘空间进行文件复制操作。

**Q: 远程VHD选择功能无法工作**
A: 检查`vhdmonter_config.ini`中`EnableRemoteSelection`是否设置为`true`，确认`BootImageSelectUrl`地址正确且服务器可访问。

**Q: 远程服务器连接失败**
A: 确认网络连接正常，检查防火墙设置，验证远程服务器是否正在运行且API接口可用。

**Q: 远程获取的关键词无效**
A: 确认远程服务器返回的JSON格式正确，`BootImageSelected`字段包含有效的关键词（SDEZ、SDHD、SDDT之一）。

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