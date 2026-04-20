# 机台日志上报三端实施方案

## 1. 结论摘要

建议按下面这条链路落地机台日志能力：

1. 机台侧保留现有本地日志文件与 U 盘导出能力。
2. 在机台程序内部新增结构化日志汇聚器与实时长连接上报通道。
3. 服务端新增独立的机台日志接收、整理、存储与检索接口。
4. Flutter 管理端新增独立的机台日志页面，支持服务端分页筛选，而不是复用当前低频审计日志页面。

核心原则：

- 不把机台运行日志直接塞进现有审计 JSONL。
- 不让前端一次性拉全量日志后本地过滤。
- 不只靠 machineId 识别机台，必须复用现有机台公私钥体系做签名鉴权。
- 日志上报必须异步、可退避、不可阻塞挂载与启动主流程。
- 只保留 `WebSocket 长连接 + 应用层加密` 这一条传输链路。
- 服务端可信性以前置“合法密文下发链路”为信任根，不再额外预置服务端签名公钥。

## 2. 为什么这样做

现有代码已经具备三块可复用基础：

- 机台侧已经有统一日志入口和本地文件日志。
- 服务端已经有机台注册、审批、公钥、吊销与审计能力。
- Flutter 管理端已经有 API 抽象、状态管理、审计筛选页骨架。

但现有审计链路不适合直接承载机台运行日志，原因如下：

- 审计日志是低频安全事件，当前读取方式是一次取最近记录。
- 机台运行日志是高频时序数据，量级和访问模式完全不同。
- 机台日志需要按时间、机台、级别、组件、事件键、关键词做服务端筛选与分页。

因此最稳妥的方案是：

- 机台运行日志走独立数据流。
- 管理员操作审计继续走现有 audit JSONL。

## 3. 现有代码基础与插入节点

### 3.1 机台侧

现有日志与身份基础：

- [Program.cs](../../src/VHDMounter/Program.cs)
  - 已初始化 `TextWriterTraceListener` 到本地 `vhdmounter.log`。
  - 已具备日志轮转与 NXLOG U 盘拷贝监控。
- [VHDManager.cs](../../src/VHDMounter/VHDManager.cs)
  - 已通过配置读取 `MachineId`、远端接口地址等信息。
  - 已有大量结构化前缀日志，如 `EVHD_MOUNT_*`、`STATUS:`。
  - 已具备 TPM/RSA 机台密钥生成与注册链路。
- [MainWindow.xaml.cs](../../src/VHDMounter/MainWindow.xaml.cs)
  - 已将 UI 状态变化通过 Trace 输出。
- [vhdmonter_config.ini](../../src/VHDMounter/vhdmonter_config.ini)
  - 已是机台端远程能力配置入口。

建议插入点：

- 在 [Program.cs](../../src/VHDMounter/Program.cs) 中日志初始化完成后，新增日志汇聚与上传后台服务。
- 在 [VHDManager.cs](../../src/VHDMounter/VHDManager.cs) 中配置读取逻辑里追加日志上报配置。
- 在 [VHDManager.cs](../../src/VHDMounter/VHDManager.cs) 中复用现有 TPM/RSA 私钥做上传签名。
- 在 [VHDManager.cs](../../src/VHDMounter/VHDManager.cs) 中将现有脱敏逻辑抽成共享方法，供上传前统一清洗。

### 3.2 服务端

现有服务端基础：

- [VHDSelectServer/server.js](../../VHDSelectServer/server.js)
  - 已有 `requireAuth`、`requireOtpStepUp`、数据库校验与统一错误模型。
  - 已有机台侧公开接口 `GET /api/evhd-envelope`，会校验机台状态、审批状态、吊销状态、公钥状态。
  - 已有管理员侧 `GET /api/audit` 审计读取接口。
- [VHDSelectServer/database.js](../../VHDSelectServer/database.js)
  - 已封装 PostgreSQL 初始化与 `machines` 表访问。
- [VHDSelectServer/init-db.sql](../../VHDSelectServer/init-db.sql)
  - 已具备数据库初始化脚本。
- [VHDSelectServer/validators.js](../../VHDSelectServer/validators.js)
  - 已有 machineId、字符串、原因等输入校验。
- [VHDSelectServer/registrationAuth.js](../../VHDSelectServer/registrationAuth.js)
  - 已有 `timestamp + nonce + signature` 的签名校验与防重放模型。
- [VHDSelectServer/auditLog.js](../../VHDSelectServer/auditLog.js)
  - 已实现 JSONL 审计日志轮转。

建议插入点：

- 在 [VHDSelectServer/server.js](../../VHDSelectServer/server.js) 新增机台日志上传接口与管理端检索接口。
- 在 [VHDSelectServer/server.js](../../VHDSelectServer/server.js) 新增机台签名鉴权中间件。
- 在 [VHDSelectServer/database.js](../../VHDSelectServer/database.js) 与 [VHDSelectServer/init-db.sql](../../VHDSelectServer/init-db.sql) 增加机台日志表。
- 在 [VHDSelectServer/validators.js](../../VHDSelectServer/validators.js) 增加 sessionId、level、component、eventKey、limit、cursor 等校验器。
- 在 [VHDSelectServer/registrationAuth.js](../../VHDSelectServer/registrationAuth.js) 的模式基础上抽出通用签名校验逻辑，供机台日志上传复用。

### 3.3 Flutter 管理端

现有前端基础：

- [vhd_mount_admin_flutter/lib/src/data.dart](../../vhd_mount_admin_flutter/lib/src/data.dart)
  - 已有 `AdminApi` 抽象与 `HttpAdminApi` 实现。
  - 已有 `AuditEntry` 模型与本地 `searchableText` 搜索文本聚合。
- [vhd_mount_admin_flutter/lib/src/state.dart](../../vhd_mount_admin_flutter/lib/src/state.dart)
  - 已有 `AppController`、`auditEntries` 和 `auditFilterMachineId` 等状态。
- [vhd_mount_admin_flutter/lib/src/dashboard.dart](../../vhd_mount_admin_flutter/lib/src/dashboard.dart)
  - 已有机器管理页、审计页、按机台过滤和搜索 UI。
  - 已支持从机台卡片一键跳转到审计视图。

建议插入点：

- 在 [vhd_mount_admin_flutter/lib/src/data.dart](../../vhd_mount_admin_flutter/lib/src/data.dart) 新增机台日志 API 与模型。
- 在 [vhd_mount_admin_flutter/lib/src/state.dart](../../vhd_mount_admin_flutter/lib/src/state.dart) 新增机台日志相关状态与加载动作。
- 在 [vhd_mount_admin_flutter/lib/src/dashboard.dart](../../vhd_mount_admin_flutter/lib/src/dashboard.dart) 新增单独的“机台日志”视图。
- 在机台列表操作中新增“查看日志”入口，与现有“查看审计”并列。

## 4. 目标架构

### 4.1 日志链路

```text
VHDMounter Trace/Status/UI 日志
  -> 结构化日志汇聚器
  -> 本地 spool 队列(JSONL 或轻量分段文件)
  -> 应用层加密 WebSocket 长连接
  -> 服务端验签、ACK、去重、整理、入库
  -> Flutter 分页筛选展示
```

### 4.2 数据职责划分

- 本地日志文件：用于机台现场排障、U 盘导出、紧急回收。
- 服务端机台日志表：用于集中查询、过滤、统计、导出。
- 服务端审计日志：只记录安全操作与日志管理行为摘要，不存高频运行明细。

### 4.3 传输层结论

本文档只保留一种传输方案：

- 底层使用 `WebSocket` 长连接。
- 加密与完整性保护全部放在应用层完成。
- 机台与服务端通过现有机台公私钥体系做身份认证。

这里要明确两件事：

- `ws://` 本身不提供加密能力。
- 真正的安全性来自“合法密文下发建立服务端信任” + 应用层握手 + ECDH 会话密钥协商 + 消息加密 + 重放防护。

因此本方案的安全边界不依赖 TLS，而依赖下面这组能力：

1. 机台 TPM/RSA 私钥签名身份认证
2. 现有合法密文下发链路提供 bootstrap secret
3. 基于 bootstrap secret 的 PSK 认证 ECDH 临时密钥交换
4. HKDF 会话密钥派生
5. AES-GCM 或 ChaCha20-Poly1305 消息加密
6. ACK、续传、nonce 与时间窗防重放

## 5. 机台侧详细方案

### 5.1 新增配置项

在 [vhdmonter_config.ini](../../src/VHDMounter/vhdmonter_config.ini) 中新增：

```ini
; 是否启用机台日志上报
EnableLogUpload=false

; 服务端 IP
MachineLogServerIp=127.0.0.1

; 服务端端口
MachineLogServerPort=8080

; 上传批间隔（毫秒）
MachineLogUploadIntervalMs=3000

; 每批最多上传多少条
MachineLogUploadBatchSize=200

; 本地待上传队列最大字节数
MachineLogUploadMaxSpoolBytes=52428800
```

说明：

- `MachineId` 仍然沿用现有配置。
- 本地配置文件只保留服务端 `IP + 端口`，不直接暴露完整 WebSocket 端点。
- WebSocket 路径由客户端与服务端协议常量固定，例如 `/ws/machine-log`，不写入配置文件。
- `machineId` 不再放在 WebSocket URL 中，而是在握手负载中显式上传。
- 本方案固定使用 WebSocket 长连接，不再维护 HTTP 批量上传或 WSS/TLS 分支。
- 心跳间隔、心跳超时、重连基线和最大退避统一由服务端握手响应下发，客户端不在本地配置中覆盖。

### 5.2 新增组件

建议新增以下机台侧类：

- `MachineLogEntry`
  - 表示单条结构化日志。
- `MachineLogBuffer`
  - 将 Trace 文本转换为结构化对象并写入本地 spool。
- `MachineLogRealtimeChannel`
  - 维护 WebSocket 长连接、按服务端策略执行心跳、重连与 ACK。
- `MachineLogSigner`
  - 复用现有 TPM/RSA 私钥构造签名头。
- `MachineLogSessionCrypto`
  - 负责密钥协商、会话密钥派生与消息加解密。
- `MachineLogSanitizer`
  - 统一做敏感字段脱敏。

### 5.3 日志结构建议

机台上送的最小对象建议如下：

```json
{
  "sessionId": "20260419T120300Z-7b1d2c",
  "seq": 1024,
  "occurredAt": "2026-04-19T12:05:10.332Z",
  "level": "info",
  "component": "VHDManager",
  "eventKey": "EVHD_MOUNT_WAIT",
  "message": "Mount point became available after 0ms",
  "rawText": "EVHD_MOUNT_WAIT: Mount point became available after 0ms",
  "metadata": {
    "targetDrive": "M:\\",
    "mountPoint": "N:\\"
  }
}
```

字段说明：

- `sessionId`
  - 单次程序启动唯一标识。
- `seq`
  - 本会话内递增序号，用于幂等去重。
- `level`
  - `debug`、`info`、`warn`、`error`。
- `component`
  - `Program`、`VHDManager`、`MainWindow`、`Updater` 等。
- `eventKey`
  - 稳定的事件类别，例如 `EVHD_MOUNT_START`、`SELF_UPDATE`、`UI_STATUS`。
- `rawText`
  - 保留脱敏后的原始文本，便于后台原样回看。

### 5.4 采集策略

建议规则：

- 保留本地 `vhdmounter.log`，不改变现有 Trace 行为。
- 在 Trace 写文件的同时，将每条日志送入结构化汇聚器。
- 对已有前缀日志做解析：
  - `STATUS:` -> `component=VHDManager`、`eventKey=STATUS`
  - `EVHD_MOUNT_*` -> `component=VHDManager`
  - `MAINWINDOW:` -> `component=MainWindow`
  - `SELF_UPDATE:` -> `component=Program`
- 未命中规则的日志统一记为 `eventKey=TRACE_LINE`。

### 5.5 上报策略

建议规则：

- 长连接传输，但日志仍按批发送，不逐条发送。
- 失败退避，不阻塞主线程。
- 服务端 ACK 后再推进本地已确认游标。
- 本地 spool 超过上限时，优先丢弃最旧的低级别日志，保留 warn/error。
- 通道自身错误不要再次写入待上传队列，避免自激增殖。
- 即使长连接在线，也仍然先写入本地 spool，再异步发送。
- 断线重连后按 `sessionId + seq` 从最后已确认位置继续补发。
- 心跳间隔、重连基线、最大退避和恢复窗口由服务端下发，客户端只负责执行，不自行定义策略。

### 5.6 应用层加密 WebSocket 方案

WebSocket 仅作为字节传输通道，鉴权和加密全部在应用层完成。

本方案的前提不是“机台预置服务端签名公钥”，而是：

- 机台已经通过现有链路向可信服务端完成机台公钥注册。
- 机台能够从服务端正常获取合法密文。
- 该合法密文中除了 EVHD 密码外，还包含专用于日志长连接握手的 bootstrap secret，或可以从现有合法密文派生出专用 bootstrap secret。

建议不要直接把 EVHD 密码原文拿来长期复用为日志通道 PSK，而是由服务端在现有密文下发响应中额外返回一份短时有效的日志通道 bootstrap 密文，例如：

- `logChannelBootstrapCiphertext`
- `logChannelBootstrapId`
- `logChannelBootstrapExpiresAt`

机台解密后得到短时有效的 `bootstrapSecret`，后续 WebSocket 握手只依赖这个短时 secret，不再依赖额外的服务端公钥预置。

建议握手流程如下：

#### 前置步骤：通过现有可信链路获取 bootstrap secret

建议扩展当前合法密文下发响应，使其除 EVHD 密文外，再返回日志通道 bootstrap 密文：

```json
{
  "ciphertext": "base64...",
  "logChannelBootstrapId": "boot_01HX...",
  "logChannelBootstrapCiphertext": "base64...",
  "logChannelBootstrapExpiresAt": "2026-04-19T12:15:00.000Z"
}
```

机台使用已注册私钥解密 `logChannelBootstrapCiphertext`，得到：

```json
{
  "bootstrapSecret": "base64...",
  "bootstrapId": "boot_01HX...",
  "expiresAt": "2026-04-19T12:15:00.000Z"
}
```

此时机台即可认定：当前掌握该 bootstrap secret 的对端，就是能正常下发合法密文的可信服务端。

#### 第一步：客户端发起握手

客户端发送未加密握手头，但关键字段必须签名：

```json
{
  "type": "client_hello",
  "machineId": "MACHINE_001",
  "keyId": "VHDMounterKey_MACHINE_001",
  "sessionId": "20260419T120300Z-7b1d2c",
  "bootstrapId": "boot_01HX...",
  "timestamp": 1776571380000,
  "nonce": "a1b2c3d4e5f6g7h8",
  "clientEcdhPublicKey": "base64...",
  "signature": "base64..."
}
```

签名内容建议覆盖：

- 协议版本
- machineId
- keyId
- sessionId
- bootstrapId
- timestamp
- nonce
- clientEcdhPublicKey

#### 第二步：服务端返回握手确认

服务端验证机台审批状态、吊销状态、签名、时间窗和 bootstrapId 后返回：

```json
{
  "type": "server_hello",
  "connectionId": "conn_01HX...",
  "bootstrapId": "boot_01HX...",
  "timestamp": 1776571380200,
  "nonce": "z9y8x7w6v5u4t3s2",
  "serverEcdhPublicKey": "base64...",
  "heartbeatSeconds": 15,
  "heartbeatTimeoutSeconds": 45,
  "reconnectBaseMs": 1000,
  "reconnectMaxMs": 30000,
  "resumeWindowSeconds": 300,
  "acknowledgedSeq": 980
}
```

这里不再要求客户端验证服务端签名公钥，而是要求后续 `client_finish/server_finish` 必须基于 bootstrap secret 对握手 transcript 做认证。

说明：

- 心跳频率由 `heartbeatSeconds` 定义。
- 客户端在 `heartbeatTimeoutSeconds` 内未收到服务端活动时，应主动断开并走重连。
- 重连退避必须按 `reconnectBaseMs -> reconnectMaxMs` 的服务端策略执行，并叠加抖动。
- 只有在 `resumeWindowSeconds` 窗口内，服务端才保证可按 ACK 位置恢复续传。

#### 第三步：派生会话密钥

双方用临时 ECDH 密钥交换后，结合 bootstrap secret，通过 HKDF 派生本次连接的认证密钥与会话密钥：

```text
sharedSecret = ECDH(clientEphemeralPrivateKey, serverEphemeralPublicKey)
authKey = HKDF(sharedSecret || bootstrapSecret, salt=clientNonce || serverNonce, info="machine-log-ws-auth-v1")
sessionKey = HKDF(sharedSecret || bootstrapSecret, salt=clientNonce || serverNonce, info="machine-log-ws-data-v1")
```

建议消息加密算法：

- AES-256-GCM
或
- ChaCha20-Poly1305

#### 第四步：完成握手认证

客户端先发送：

```json
{
  "type": "client_finish",
  "mac": "base64..."
}
```

其中 `mac = HMAC(authKey, transcriptHash || "client_finish")`

服务端验证通过后返回：

```json
{
  "type": "server_finish",
  "mac": "base64..."
}
```

其中 `mac = HMAC(authKey, transcriptHash || "server_finish")`

只有 bootstrap secret 正确、ECDH transcript 未被篡改时，双方才能同时完成握手。

#### 第五步：加密传输业务帧

后续业务帧统一走加密信封：

```json
{
  "type": "encrypted_frame",
  "seq": 981,
  "ack": 980,
  "iv": "base64...",
  "ciphertext": "base64...",
  "tag": "base64..."
}
```

业务负载类型建议包括：

- `log_batch`
- `ack`
- `heartbeat`
- `resume`
- `rekey`
- `close`

#### 第六步：重连与续传

必须支持：

- 服务端 ACK 最高连续 `seq`
- 客户端断线后带 `sessionId` 和本地最大 `seq` 重连
- 从最后已确认 `seq` 开始补发
- 定期重新协商会话密钥

这个方案的目标是：即使底层是 `ws://` 明文，只要服务端已经通过合法密文下发链路被确认可信，后续日志通道仍保持机密性、完整性与可恢复性。

## 6. 服务端详细方案

### 6.1 数据库设计

建议新增三类存储对象：

- 机台日志会话表
- 机台日志明细表
- 服务端运行时配置表

#### machine_log_sessions

建议字段：

- `id`
- `machine_id`
- `session_id`
- `app_version`
- `os_version`
- `started_at`
- `last_upload_at`
- `last_event_at`
- `total_count`
- `warn_count`
- `error_count`
- `last_level`
- `last_component`
- `created_at`
- `updated_at`

唯一键：

- `(machine_id, session_id)`

#### machine_log_entries

建议字段：

- `id`
- `machine_id`
- `session_id`
- `seq`
- `occurred_at`
- `log_day`
- `received_at`
- `level`
- `component`
- `event_key`
- `message`
- `raw_text`
- `metadata_json`
- `upload_request_id`
- `created_at`

唯一键：

- `(machine_id, session_id, seq)`

建议索引：

- `(machine_id, occurred_at desc)`
- `(machine_id, log_day desc)`
- `(session_id, seq)`
- `(level, occurred_at desc)`
- `(component, occurred_at desc)`
- `(event_key, occurred_at desc)`

其中：

- `log_day` 表示按服务端统一时区归一化后的日志活动日，建议由 `occurred_at` 截断到日期得到。
- 后续清理策略按 `machine_id + log_day` 执行，而不是按自然时间 TTL 直接删除。

另外，建议在现有 `machines` 管理数据上增加机台级保留策略覆盖字段：

- `log_retention_active_days_override`

用途：

- 为空时表示继承服务端全局默认保留活动日配置。
- 有值时表示该机台单独使用自己的保留活动日上限。

#### service_runtime_settings

建议新增一张服务端运行时配置表，用于存放初始化后真正生效的默认值，而不是把这些默认值长期留在外部配置中。

建议字段：

- `setting_key`
- `setting_value_json`
- `updated_by`
- `updated_at`

建议初始键：

- `machine_log.default_retention_active_days`
- `machine_log.daily_inspection_hour`
- `machine_log.daily_inspection_minute`
- `machine_log.timezone`
- `machine_log.last_inspection_at`

建议规则：

- 服务端初始化完成后，即使这些值只是默认值，也立即写入数据库。
- 后续服务端只从数据库读取这些值，不再依赖外部配置暴露默认值。
- 如果后续升级新增新的日志配置项，也通过迁移或启动自检把缺失默认值补写入数据库。

### 6.2 新增接口

#### 1. WebSocket 长连接接口

`GET /ws/machine-log`

用途：

- 机台建立实时日志上报长连接。

建议：

- 固定使用上一节定义的应用层加密握手与消息加密，不能把明文日志直接送进去。
- 本地配置文件不保存该完整端点，只保存 `IP + 端口`；客户端内部拼接固定路径。
- 长连接建立成功后，日志仍以批为单位发送，不建议逐行即时推送。

#### 2. 合法密文下发接口扩展

建议扩展现有服务端密文下发响应，在当前机台已信任的密文链路中追加日志握手 bootstrap 数据，而不是另起一条新的服务端认证链路。

建议新增返回字段：

- `logChannelBootstrapId`
- `logChannelBootstrapCiphertext`
- `logChannelBootstrapExpiresAt`

#### 3. 会话列表接口

`GET /api/machine-log-sessions`

查询参数建议：

- `machineId`
- `from`
- `to`
- `limit`

用途：

- 给前端展示某机台有哪些启动会话。

#### 3A. 日志保留策略接口

`GET /api/settings/log-retention`

用途：

- 获取服务端当前默认日志保留策略。

权限建议：

- 仅已登录管理员可访问。
- 如需进一步收口，可要求 OTP 二次验证后才允许读取和修改。
- 不允许在 `GET /api/status`、`GET /api/health`、`GET /api/init/status` 这类公开或半公开状态接口中返回这些默认值。

建议返回：

- `defaultRetentionActiveDays`
- `dailyInspectionHour`
- `dailyInspectionMinute`
- `timezone`
- `lastInspectionAt`

`POST /api/settings/log-retention`

用途：

- 管理员修改全局默认日志保留活动日和每日巡检时间。

说明：

- 修改后直接写入 `service_runtime_settings`。
- 不再回写到环境变量、启动参数或公开配置文件。

`POST /api/machines/:machineId/log-retention`

用途：

- 为单台机设置或清除日志保留活动日覆盖值。

建议请求字段：

- `retentionActiveDaysOverride`

规则：

- `null` 表示恢复继承全局默认值。
- 正整数表示该机台独立保留多少个活动日志日。

#### 4. 日志明细接口

`GET /api/machine-logs`

查询参数建议：

- `machineId`
- `sessionId`
- `level`
- `component`
- `eventKey`
- `q`
- `from`
- `to`
- `cursor`
- `limit`

用途：

- 给前端做服务端分页与筛选。

#### 5. 导出接口

`GET /api/machine-logs/export`

用途：

- 导出原始日志文本或 JSON。

建议：

- 该接口要求登录且完成 OTP 二次验证。

### 6.3 服务端整理逻辑

建议在入库时就完成整理，而不是查询时临时计算：

- 标准化 `level`、`component`、`eventKey`。
- 多行异常堆栈合并策略。
- 更新 session 汇总计数。
- 记录接收时间与上传请求 ID。
- 对消息和 metadata 做长度截断与格式清理。

### 6.4 服务端日志保留上限

服务端需要增加日志保留上限，但这里的“七天”不是指自然时间连续七天，而是：

- 每个机台最多保留最近 `N` 个“有日志写入的活动日”。
- `N` 由数据库中的服务端运行时配置定义。
- 初始化完成后，即使 `N` 只是默认值，也已经作为正式配置写入数据库。
- 机台可单独覆盖自己的 `N` 值，例如某些重点机台保留 `30` 个活动日，普通机台保留 `7` 个活动日。
- 只有当某个机台累计出现的活动日数量超过它自己的保留上限时，才开始清理更早的数据。
- 如果机台长期不上线，没有新的日志活动日产生，就不会触发清理。

这条规则的效果是：

- 机台不上线时，旧日志不会因为自然时间流逝被提前清掉。
- 机台持续上线并持续产生日志时，服务端占用会稳定压在该机台自己的保留活动日上限内。
- 管理员可以在不改代码的情况下调整全局默认值，并对单机做差异化覆盖。

#### 规则示例

如果某机台默认保留 `7` 个活动日志日，并且：

- 第 1 天有日志
- 第 2 天有日志
- 第 3 天关机，无日志
- 后续再连续 7 个活动日有日志

那么保留集应当始终是“最近 7 个有日志的活动日”，而不是“最近 7 个自然日”。

也就是说：

- 第 8 个活动日到来之前，不做清理。
- 从第 8 个活动日起，开始清理早于最近 7 个活动日的数据。

#### 建议实现方式

建议按机台维度执行清理，逻辑如下：

1. 从 `service_runtime_settings` 读取服务端全局默认保留值 `defaultRetentionActiveDays`。
2. 读取该机台的 `log_retention_active_days_override`，若存在则以该值作为当前机台的保留上限。
3. 查询该机台所有出现过日志的 `log_day`，按时间倒序排列。
4. 如果不同的 `log_day` 数量小于等于当前机台保留上限，则不清理。
5. 如果数量大于当前机台保留上限，则保留最新的对应数量 `log_day`。
4. 删除该机台所有 `log_day` 早于保留集合最早一天的日志记录。
5. 对已经没有任何明细日志的旧 `session` 做级联清理或孤儿会话清理。

SQL 思路可以写成按单机执行的参数化清理：

```sql
WITH ranked_days AS (
  SELECT
    machine_id,
    log_day,
    DENSE_RANK() OVER (
      PARTITION BY machine_id
      ORDER BY log_day DESC
    ) AS day_rank
  FROM machine_log_entries
  GROUP BY machine_id, log_day
)
DELETE FROM machine_log_entries e
USING ranked_days d
WHERE e.machine_id = d.machine_id
  AND e.log_day = d.log_day
  AND d.day_rank > $retentionActiveDays;
```

#### 触发时机

建议固定为每日巡检任务：

- 不在写入新日志时触发清理，避免把写入路径和保留策略耦合在一起。
- 服务端每天固定时间执行一次保留策略巡检，按机台逐个计算并清理。
- 同一次巡检中顺带处理孤儿 session 和无明细的历史会话。

这样做的好处：

- 写入路径更稳定，日志上报不会因为额外清理 SQL 受到抖动影响。
- 管理员修改保留天数后，不需要等机台再次上传新日志才生效，下一次每日巡检就能统一应用。
- 每台机的差异化保留策略可以在巡检时一次性生效。

#### 配置建议

服务端配置建议改成“初始化后全部入库的运行时配置”：

- 初始化完成时，把日志保留默认值、每日巡检时间、时区等配置立即写入 `service_runtime_settings`。
- 真正生效的值只从数据库读取。
- 机台级覆盖值存放在机台配置记录中。
- 外部环境变量、公开状态接口和静态配置文件中尽量不暴露这些默认值。

建议实现规则：

1. 初始化流程完成后，如果数据库中不存在 `machine_log.*` 相关配置项，则自动写入一组默认记录。
2. 启动时若发现数据库已有这些配置项，则直接使用数据库值。
3. 后续管理员所有修改都只更新数据库，不再依赖服务重启或修改环境变量。
4. 公开状态接口不返回这些配置项，避免无关端点暴露默认值和运维策略。

### 6.5 审计日志与机台日志的关系

机台日志不直接写入现有 `server-audit.log` 明细。

审计日志只记录摘要事件，例如：

- `machine.log.upload`
- `machine.log.export`
- `machine.log.view.raw`

这样可以保住审计日志的可读性与边界。

## 7. Flutter 管理端详细方案

### 7.1 新增数据模型

在 [vhd_mount_admin_flutter/lib/src/data.dart](../../vhd_mount_admin_flutter/lib/src/data.dart) 中新增：

- `MachineLogSession`
- `MachineLogEntry`
- `MachineLogFilter`

### 7.2 新增 API 抽象

在 `AdminApi` 中新增：

- `Future<List<MachineLogSession>> getMachineLogSessions(...)`
- `Future<MachineLogPage> getMachineLogs(...)`
- `Future<String> exportMachineLogs(...)`

其中 `MachineLogPage` 建议包含：

- `entries`
- `nextCursor`
- `hasMore`

### 7.3 新增状态管理

在 [vhd_mount_admin_flutter/lib/src/state.dart](../../vhd_mount_admin_flutter/lib/src/state.dart) 中新增：

- `machineLogSessions`
- `machineLogEntries`
- `machineLogSelectedMachineId`
- `machineLogSelectedSessionId`
- `machineLogLevel`
- `machineLogComponent`
- `machineLogEventKey`
- `machineLogQuery`
- `machineLogCursor`
- `machineLogHasMore`

新增动作：

- `loadMachineLogSessions(...)`
- `loadMachineLogs(...)`
- `loadMoreMachineLogs()`
- `clearMachineLogFilters()`

### 7.4 新增页面与交互

建议在 [vhd_mount_admin_flutter/lib/src/dashboard.dart](../../vhd_mount_admin_flutter/lib/src/dashboard.dart) 新增“机台日志”页，而不是把它并入现有“审计”页。

页面结构建议：

1. 顶部筛选区
   - 机台
   - 会话
   - 级别
   - 组件
   - 事件键
   - 时间范围
   - 关键词搜索
2. 左侧或顶部会话列表
   - 显示每次启动时间、最近日志时间、warn/error 数量
3. 中间主列表
   - 显示时间、级别、组件、事件键、摘要消息
4. 右侧详情面板
   - 显示原始文本与 metadata

### 7.5 日志保留策略配置页

前端需要新增一块明确的“日志保留策略”管理界面，建议放在设置页下，内容分成两层：

#### 全局默认配置

- 默认保留活动日数
- 每日巡检执行时间
- 当前服务端时区
- 最近一次巡检时间

管理员可以在这里修改：

- 所有未单独覆盖机台的默认保留活动日数
- 每日巡检任务的执行时间

#### 机台差异化覆盖配置

在机台管理页或机台详情侧边栏中增加“日志保留”配置项：

- 当前生效值
- 是否继承全局默认值
- 覆盖值输入框
- 清除覆盖按钮

建议交互：

- 机器列表上直接展示“日志保留：继承 7 天”或“日志保留：30 天覆盖”。
- 点击机台详情后可修改该机台的覆盖值。
- 前端应明确区分“活动日志日”与“自然日”，避免管理员误解。

### 7.6 与现有页面的衔接

在机台管理卡片上增加一个并列动作：

- 查看审计
- 查看日志

点击“查看日志”后：

- 自动带上 machineId 打开机台日志页。

## 8. 接口安全措施

### 8.1 机台身份认证

不能只依赖 `machineId`。

必须复用现有机台公私钥体系：

- 机台侧用 TPM/RSA 私钥签名。
- 服务端用已审批机台的 `pubkey_pem` 验签。
- 若机台未审批、已吊销、未注册公钥，直接拒绝上传。

### 8.2 签名头建议

建议请求头包含：

- `X-Machine-Id`
- `X-Key-Id`
- `X-Timestamp`
- `X-Nonce`
- `X-Content-SHA256`
- `X-Signature`

签名串建议覆盖：

- HTTP 方法
- 请求路径
- machineId
- keyId
- timestamp
- nonce
- body sha256
- sessionId

### 8.3 防重放

复用现有注册签名链路的设计思路：

- `timestamp` 允许偏差最多 5 分钟。
- `nonce` 缓存 10 分钟。
- 同一个 `nonce` 重复提交直接拒绝。

### 8.4 传输安全

- 固定使用 `WS + 应用层端到端加密`。
- `WS` 明文长连接只能作为承载加密信封的字节通道，不能直接传原始日志内容。
- 服务端可信性来自现有合法密文下发链路，不再额外预置服务端签名公钥。
- WebSocket 握手必须显式绑定 `bootstrapSecret + ECDH transcript`，不能做裸 ECDH 协商。
- 客户端不在本地配置中暴露完整端点、心跳间隔和重连策略，避免配置文件泄漏带来额外侧信道信息。
- 拒绝未加密的生产日志明文上传。

### 8.5 限流与限额

上传接口至少做：

- 每机台限流
- 每 IP 限流
- 单批最大条数限制
- 单条最大长度限制
- 单日最大体积限制
- 每机台最多保留最近 N 个活动日志日，N 由全局默认值或机台覆盖值决定

### 8.6 日志脱敏

机台上送前必须脱敏，重点清洗：

- password
- token
- authorization
- ciphertext
- sessionSecret
- registrationCertificatePassword
- 其他密钥或票据字段

服务端入库前可再次兜底清洗一次。

### 8.7 管理端权限边界

- 普通查看日志列表：要求登录。
- 导出原始日志、批量跨机台导出、查看高敏感原文：要求 OTP 二次验证。

### 8.8 展示安全

- 原始日志一律按纯文本展示。
- 不做 HTML 渲染。
- 不信任 metadata 中的任何富文本内容。

## 9. 研发实施顺序

建议按四个阶段推进。

### 阶段 1：机台侧实时链路

- 新增结构化日志对象。
- 新增本地 spool。
- 新增加密 WebSocket 通道。
- 验证体积、频率、脱敏效果、ACK 与断线补传。

### 阶段 2：服务端接收与查询

- 新增数据库表。
- 新增 WebSocket 接入点。
- 新增查询接口。
- 新增签名验签中间件。
- 新增连接会话状态与 ACK 管理。
- 补齐应用层密钥协商与帧加解密。
- 验证断线续传、重放拒绝、重连退避与单机唯一活跃连接策略。
- 实现每日巡检型日志清理任务。
- 实现全局默认保留活动日配置与机台级覆盖配置。

### 阶段 3：Flutter 管理端展示

- 新增机台日志页面。
- 接入分页筛选与会话视图。
- 在机台卡片加“查看日志”跳转。
- 新增日志保留策略设置页和机台级覆盖配置入口。

### 阶段 4：安全与运维加固

- 限流
- 数据保留策略
- 原始日志导出权限控制
- 告警与容量监控
- 孤儿 session 清理与保留策略巡检
- 每日巡检任务失败告警与重试策略

## 10. 验收标准

机台侧：

- 网络不可用时不影响挂载和启动。
- 本地日志仍可正常写入与导出。
- 恢复网络后可自动补传。

服务端：

- 能基于 `machineId + sessionId + seq` 正确去重。
- 未审批机台、已吊销机台、签名错误、重放请求均被拒绝。
- 查询接口支持服务端分页与筛选。
- 每机台只保留最近 N 个活动日志日，且 N 支持全局默认值与机台级覆盖值。
- 日志清理仅由每日巡检任务触发，不在实时写入路径执行。
- 管理员修改保留天数后，下一次每日巡检即可生效。

Flutter 端：

- 可按机台、会话、级别、组件、事件键、关键词筛选。
- 支持查看原始日志详情。
- 大批量数据下仍保持流畅，不依赖一次性全量加载。
- 可配置全局默认日志保留活动日与单机覆盖值，并能清楚看到当前生效策略。

## 11. 明确不建议的做法

- 不建议把机台运行日志直接写进现有 `GET /api/audit` 对应的数据源。
- 不建议让前端全量拉取机台日志后做本地搜索。
- 不建议只凭 `machineId` 做机台上传鉴权。
- 不建议让通道失败日志继续回灌上传队列。
- 不建议在没有服务端身份校验的前提下自定义应用层加密握手。

## 12. 最终建议

最终建议是：

- 机台日志单独建链路。
- 服务端单独建表与 API。
- 前端单独建页面。
- 安全上完全复用现有机台公钥注册、审批、吊销、签名与防重放体系。
- 传输层固定为“WebSocket 长连接 + 应用层加密 + ACK + 断线补传”。

这是当前代码基础上依赖最少、风险最低、后续可维护性最好的方案。