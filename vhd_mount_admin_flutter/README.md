# VHD Mount Admin Flutter

Windows Flutter 桌面管理客户端，用于连接 VHD Mount 服务并完成初始化、管理员登录、机台管理、可信注册证书维护、审计查询和安全设置操作。

## 主要功能

- 连接服务端并检测初始化状态
- 完成首次初始化，包括 OTP、管理员口令、数据库配置和可信注册证书导入
- 管理机台审批、保护状态、启动关键词与 EVHD 密码
- 查看可信注册证书和审计日志
- 执行 OTP 二次验证、OTP 轮换和管理员密码修改

## 开发命令

- `flutter pub get`
- `flutter run -d windows`
- `flutter analyze`
- `flutter test`

## 项目结构

- `lib/main.dart`：最小启动壳
- `lib/app.dart`：应用库入口
- `lib/src/core.dart`：主题、共享控件和基础工具
- `lib/src/data.dart`：模型、配置存储和 HTTP API
- `lib/src/state.dart`：`AppController` 状态编排
- `lib/src/shell.dart`：应用外壳、连接、初始化和登录流程
- `lib/src/dashboard.dart`：管理台主页面与各功能页
- `lib/src/dialogs.dart`：复用对话框
- `test/`：按控制器、服务、存储、页面分层的回归测试

## 维护约定

- 新的业务逻辑优先按模块放入 `lib/src/` 对应文件，而不是回填到启动壳
- 变更网络或配置读取逻辑后，优先补充 `test/services` 或 `test/storage` 回归测试
- 提交前至少运行一次 `flutter analyze` 和 `flutter test`
