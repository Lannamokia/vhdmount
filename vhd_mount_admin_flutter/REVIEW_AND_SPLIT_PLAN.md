# Flutter 管理客户端审查与拆分计划

日期：2026-04-17

## 1. 范围与约束

- 范围仅覆盖 vhd_mount_admin_flutter。
- 本轮只做代码审查、问题核对和拆分计划整理，不改动业务实现。
- 计划以“最低回归风险”为目标，不在拆分第一阶段同时更换状态管理方案、网络层抽象或 UI 设计语言。

## 2. 已核对基线

- 代码规模：lib 目录当前只有 main.dart，约 5146 行。
- 测试规模：test 目录当前只有 widget_test.dart。
- 静态分析：flutter analyze 通过，无分析器报错。
- 测试结果：flutter test 通过，13 个测试全部通过。

结论：当前主要问题不是“项目跑不起来”，而是单文件巨石、职责耦合、行为回归测试覆盖不足，以及若干实现细节的可维护性风险。

## 3. 已证实的问题

### 3.1 机器页跳转审计页时，机台过滤会被切页回调覆盖

- 证据链：DashboardScreen 的 openAuditForMachine 先调用 loadAudit(machineId)；随后 onDestinationSelected(2) 又触发一次无参 loadAudit()。
- 结果：用户从机器页点击“查阅审计日志”时，原本应保留的 machineId 过滤会被清空，最终看到的是全量审计而不是目标机台审计。
- 影响：这是已存在的真实行为缺陷，不是纯架构问题。
- 优先级：P1。

### 3.2 生产代码已经形成单文件巨石，职责边界失控

- 现状：main.dart 同时承载主题、工具函数、平台输入适配、配置持久化、HTTP API、状态控制器、应用壳、4 个主页面、通用组件、对话框和模型。
- 结果：任何改动都会扩大回归面，测试也只能整体 import main.dart，导致定位问题和局部维护成本持续上升。
- 影响：这是目前最核心的维护性问题。
- 优先级：P1。

### 3.3 测试覆盖偏向布局与冒烟，缺少关键行为回归

- 现状：widget_test.dart 同时容纳控制器测试、页面测试、响应式测试和 FakeAdminApi/FakeClientConfigStore。
- 已缺失的关键覆盖：机器页跳审计页的过滤保持、Dashboard 切页后的数据装载行为、ConnectionScreen 路由、Settings 关键操作链路、Audit 筛选与搜索联动。
- 结果：真实行为缺陷可以在“13 个测试全绿”的情况下漏过。
- 优先级：P1。

### 3.4 本地配置读取静默吞掉异常，损坏配置会伪装成“未配置”

- 现状：FileClientConfigStore.load 将文件不存在、空文件、JSON 损坏、权限异常等情况统一回退为空配置。
- 结果：用户配置损坏时，界面只会表现成“没有历史地址”，而不是给出可诊断信号。
- 影响：排障困难，错误来源不透明。
- 优先级：P2。

### 3.5 项目文档和元数据仍保留模板内容

- 现状：README 仍是 Flutter 新项目模板；pubspec description 仍是 A new Flutter project.
- 结果：仓库无法直接反映该管理客户端的职责、运行方式和维护入口。
- 影响：交接和维护门槛偏高。
- 优先级：P3。

## 4. 待确认风险

以下问题具备合理风险迹象，但需要结合后端约束或产品决策再定是否纳入后续修复：

### 4.1 传输层默认使用 HTTP 且未见请求超时控制

- 结论更新：部署环境已明确严格限定在受控内网，因此默认使用 HTTP 不再作为当前版本的高优先级安全风险处理。
- 保留风险：即便不作为外网传输风险处理，请求缺少超时控制仍然会带来可用性问题，例如服务端卡顿时界面长时间等待、错误恢复不明确。
- 处理建议：本项从“待确认风险”降级为“低优先级技术债”，后续优先补请求超时、重试/取消策略，以及在需要跨网段部署时再补 HTTPS 支持。

### 4.2 多个路径参数直接字符串插值，未统一做路径编码

- 风险：如果 machineId 或 fingerprint 允许更宽字符集，路径解析可能出错。
- 待确认项：后端是否已经强约束这些字段只能使用 URL-safe 字符。

### 4.3 bootstrap 失败时会保留旧状态

- 风险：如果不是有意保留 last known good UI，刷新失败后继续展示旧服务数据可能误导用户。
- 待确认项：这是设计选择，还是缺少失败态清理。

### 4.4 lint 规则基本等于默认模板

- 风险：对于 5000+ 行单文件项目，过于宽松的静态规则不利于控制复杂度继续增长。
- 待确认项：团队是否愿意接受更严格的 lint 作为后续治理手段。

## 5. 拆分目标

### 5.1 总体目标

- 将单文件 main.dart 拆成清晰模块，但不在第一阶段改变现有运行行为。
- 先做等价搬运和职责归位，再考虑进一步拆分状态控制器。
- 让 main.dart 在过渡期继续作为兼容入口，直到测试和依赖都迁稳。

### 5.2 非目标

- 第一阶段不切换到 Riverpod、Bloc、Redux 等新状态管理框架。
- 第一阶段不重写 API 分层，也不引入 repository/usecase 全家桶。
- 第一阶段不顺手做 UI 重设计。

## 6. 目标目录结构

```text
lib/
  main.dart
  app.dart
  src/
    bootstrap/
      admin_app.dart
      admin_root.dart
      splash_screen.dart
    theme/
      app_palette.dart
      app_theme.dart
    platform/
      windows_secure_input.dart
    utils/
      error_utils.dart
      url_utils.dart
      secret_utils.dart
    formatters/
      audit_formatters.dart
      audit_event_describer.dart
    models/
      server_status.dart
      auth_status.dart
      otp_status.dart
      initialization_preparation.dart
      client_config.dart
      machine_record.dart
      machine_draft.dart
      trusted_certificate_record.dart
      audit_entry.dart
      audit_event_presentation.dart
    services/
      admin_api.dart
      http_admin_api.dart
      client_config_store.dart
      file_client_config_store.dart
    controllers/
      app_controller.dart
    widgets/
      admin_backdrop.dart
      app_panel.dart
      accent_icon_badge.dart
      auth_shell.dart
      page_header.dart
      overview_stat_card.dart
      overview_stats_grid.dart
      dashboard_sidebar_button.dart
      secure_text_field.dart
      server_address_field.dart
      info_panel.dart
      error_banner.dart
      status_chip.dart
    dialogs/
      single_input_dialog.dart
      add_machine_dialog.dart
      confirm_dialog.dart
    screens/
      connection/
        connection_screen.dart
      initialization/
        initialization_screen.dart
      auth/
        login_screen.dart
      dashboard/
        dashboard_screen.dart
        dashboard_destination_spec.dart
        machines_view.dart
        certificates_view.dart
        audit_view.dart
        settings_view.dart
test/
  support/
    fake_admin_api.dart
    fake_client_config_store.dart
  widgets/
    admin_root_test.dart
    dashboard_test.dart
    audit_view_test.dart
    settings_view_test.dart
  controllers/
    app_controller_test.dart
  storage/
    file_client_config_store_test.dart
```

## 7. 分阶段执行计划

### 阶段 0：补安全网测试

目标：在不改生产代码结构的前提下，先把现有行为锁住。

任务：

- 将 FakeAdminApi、FakeClientConfigStore 从 widget_test.dart 提取到 test/support。
- 为以下行为新增测试：
  - MachinesView 点击“查阅审计日志”后，AuditView 仍保留 machineId 过滤。
  - AdminRoot 在 serverStatus 为 null 时进入 ConnectionScreen。
  - Dashboard 四个标签切换时的装载行为。
  - AuditView 的筛选与本地搜索。
  - SettingsView 的关键交互链路。

完成标准：

- flutter test 通过。
- 新增测试能稳定复现并保护当前已证实缺陷与关键路由行为。

### 阶段 1：抽纯模型、纯函数、主题与格式化逻辑

目标：先迁走无副作用、低耦合代码，降低 main.dart 噪声。

任务：

- 抽出 models。
- 抽出 AppPalette、ThemeData 相关代码。
- 抽出 normalizeBaseUrl、normalizeOtpauthUrl、generateSessionSecret、describeError 等工具函数。
- 抽出审计格式化与事件文案映射。

完成标准：

- 所有类型名、构造函数、getter 语义保持不变。
- flutter analyze 与 flutter test 仍全绿。

### 阶段 2：抽基础设施层

目标：把 IO 边界从 main.dart 拿出去，但不改变接口形状。

任务：

- 抽出 AdminApi 与 HttpAdminApi。
- 抽出 ClientConfigStore 与 FileClientConfigStore。
- 抽出 Windows 安全输入平台代码。

完成标准：

- bootstrap、登录、OTP、地址记忆相关测试通过。
- flutter analyze 与 flutter test 全绿。
- 至少补做一次 flutter build windows 验证平台通道未回归。

### 阶段 3：抽 AppController，但暂不拆多控制器

目标：保留现有控制器外观，先完成物理拆分，再考虑状态职责细分。

任务：

- 将 AppController 整体迁到 controllers/app_controller.dart。
- 暂时不改变 public method、字段和加载顺序。
- 保持 ChangeNotifier 方案不变。

完成标准：

- 现有页面和测试调用方式不需要大改。
- bootstrap、OTP 过期、机器加载、证书加载、审计加载行为一致。

### 阶段 4：抽共享组件与对话框

目标：将跨页面复用的 UI 叶子组件独立出来，降低页面文件体积。

任务：

- 抽出 AuthShell、PageHeader、OverviewStatCard、OverviewStatsGrid、DashboardSidebarButton、SecureTextField、InfoPanel、ErrorBanner、StatusChip 等共享组件。
- 抽出单输入、添加机台、确认操作等对话框。

完成标准：

- 现有响应式布局测试继续通过。
- 登录页、Dashboard 和设置页的视觉结构无行为回归。

### 阶段 5：按路由与功能页拆屏幕模块

目标：将页面级代码按真实业务边界拆开。

推荐顺序：

1. ConnectionScreen
2. InitializationScreen
3. LoginScreen
4. MachinesView
5. AuditView
6. CertificatesView
7. SettingsView
8. DashboardScreen
9. AdminRoot
10. AdminApp

理由：

- 先拆叶子页，最后拆壳层，回归风险最低。
- MachinesView 和 AuditView 之间已经存在真实联动缺陷，优先拆这两块更有价值。

完成标准：

- main.dart 仍可作为兼容入口。
- 各页面的导入依赖清晰，不再互相夹带大量共享代码。

### 阶段 6：收口兼容层

目标：让 main.dart 只保留启动职责，测试改为依赖稳定导出入口。

任务：

- 新增 app.dart 作为稳定入口导出。
- 调整测试与其他引用，避免继续直接 import main.dart。
- 将 main.dart 缩减为 main() 与最薄的装配代码。

完成标准：

- main.dart 不再承载业务类和页面实现。
- flutter analyze、flutter test、flutter build windows 全绿。

## 8. 建议先补的测试清单

按优先级排序：

1. 机器页跳审计页后的过滤保持测试。
2. AppController.bootstrap 在已认证与未认证两种状态下的编排测试。
3. Dashboard 切页触发数据装载的测试。
4. AuditView 机台筛选、搜索、清空筛选测试。
5. SettingsView 的默认关键词更新、OTP rotate、修改密码测试。
6. FileClientConfigStore 的空文件、坏 JSON、正常 round-trip 测试。
7. 轻量页面烟雾测试分拆到各自 feature 文件，避免继续堆在 widget_test.dart。

## 9. 迁移时的强约束

- 先做文件级模块化，不先做架构级重写。
- 早期阶段只允许“等价搬运”，不顺手修改行为。
- AppController 在拆分前中期继续作为唯一编排中枢，避免多控制器并行改动造成状态漂移。
- main.dart 在大部分迁移阶段继续承担 façade 角色，直到测试与导入方稳定迁移完成。
- 每个阶段都至少运行 flutter analyze 和 flutter test；涉及平台代码时补跑 flutter build windows。

## 10. 建议的执行顺序总结

如果只看最稳妥路径，推荐按下面顺序推进：

1. 先补测试安全网。
2. 再抽纯模型和纯工具。
3. 再抽 API、配置存储和平台代码。
4. 再抽 AppController。
5. 再抽共享组件与对话框。
6. 再拆各个页面。
7. 最后收口 main.dart 与测试入口。

这条路径的核心原则是：先削减文件体积，再收紧职责边界，最后才处理更深的架构演进。