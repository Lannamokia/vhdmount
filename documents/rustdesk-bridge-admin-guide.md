# RustDesk 远程控制桥接指南

本文档面向机房管理员，说明如何通过 VHD Mounter 的 RustDesk 桥接功能，对机台进行受控、可审计的远程协助。

::: warning 安全提示
RustDesk 桥接涉及远程控制凭证的集中管理，属于高敏感功能。所有管理端操作均需要 OTP 二次验证，所有读取明文密码的行为均会写入审计日志。
:::

## 功能概述

VHD Mounter 机台端内建了一个命名管道桥接服务，与本地 RustDesk 受控端协作：

- 机台侧只接受来自本机 RustDesk 进程的连接请求
- 哪些主控端可以连接，由服务端统一下发“可信主控端快照”
- RustDesk 连接密码在机台端加密后上报服务端，管理员需要时通过管理客户端读取
- 共享密钥和主控端列表变更时，服务端会主动通知机台刷新

整体架构保证：没有服务端授权，机台不会接受任何远程控制；所有授权和读取行为都可被审计。

## 前置条件

- 机台运行 `VHDMounter.exe` 或 `VHDMounter_Maimoller.exe`
- 服务端版本 ≥ 1.7.0，且已完成初始化
- 机台端 `vhdmonter_config.ini` 已启用 RustDesk 桥接
- RustDesk 受控端（`RustDesk_Controlled`）已部署在机台上，并能连接到命名管道

::: danger 兼容性声明
RustDesk 桥接功能**仅兼容** [Lannamokia/rustdesk](https://github.com/Lannamokia/rustdesk) 二次开发客户端。官方原版 RustDesk 或其他衍生版本均不在支持范围内。该项目的详细中文说明与编译部署指南请参见 [README-ZH.md](https://github.com/Lannamokia/rustdesk/blob/master/docs/README-ZH.md)。

该二次开发客户端与上游保持安全同步策略：无重大 CVE 时不同步上游更新，仅保留**基础远程控制**和**文件传输**两类核心功能，其余功能均裁剪移除。部署前请确认机台上安装的是该指定版本。
:::

## 客户端部署与编译依赖

### 自建 RustDesk 中转服务器

二次开发客户端**不依赖**官方公共中继，需要管理员自行搭建 RustDesk 中转服务：

| 组件 | 说明 |
|------|------|
| `hbbs` | ID 注册与心跳服务，为受控端分配 RustDesk ID |
| `hbbr` | 中继服务，在主控端与受控端之间转发加密流量 |

部署时请将 `hbbs` 与 `hbbr` 部署在可被机台访问的服务器上，并妥善保存 `hbbs` 生成的公钥。机台客户端需要内置这些连接信息才能上线和被控。

### 编译注入参数

从源码编译受控端客户端时，必须通过 CI 的 **Repository secrets** 注入以下四个参数：

| Secret | 说明 |
|--------|------|
| `HBBR_HOST` | 自建 `hbbr` 中继服务器的地址或域名 |
| `HBBS_HOST` | 自建 `hbbs` ID 服务器的地址或域名 |
| `HBBS_KEY` | `hbbs` 生成的公钥，用于受控端验证服务器身份 |
| `VHD_BRIDGE_SECRET_HEX` | 与 `vhdmonter_config.ini` / 服务端对应的桥接共享密钥，64 位十六进制 |

编译脚本会在构建受控端时读取上述 secrets，并将对应值硬编码到客户端中。请确保：

- 四个参数与生产环境实际部署的中转服务器、桥接密钥完全一致；
- `VHD_BRIDGE_SECRET_HEX` 与服务端录入的 active `RustDeskClientSharedSecret` 版本匹配；
- 不要将 secrets 随客户端分发给非授权人员。

## 机台端配置

编辑机台上的 `vhdmonter_config.ini`：

```ini
[Settings]
; 启用 RustDesk 桥接总开关
EnableRustDeskBridge=true

; 策略公钥本地缓存路径（可选）
; 留空时启动期自动从服务端拉取
BridgePolicyPubkeyPath=

; 可信主控端快照拉取周期，单位秒，默认 300
SnapshotPullIntervalSeconds=300

; 报告上行超时，单位毫秒，默认 5000
ReportUpstreamTimeoutMs=5000

; 报告重试队列容量，默认 64
ReportRetryQueueCapacity=64

; 命名管道并发服务客户端数，默认 1
HandshakeNonceLruClientCount=1

; 反向撤销通道监听端口，默认 7891，仅监听 127.0.0.1
BridgeRevocationListenPort=7891
```

配置完成后重启机台客户端生效。

## 服务端管理操作

全部管理操作均通过 Flutter 管理客户端完成；管理端页面位于 **设置 → RustDesk 远程控制**（或根据客户端版本在导航中直接显示为“远程控制”）。

### 1. 录入 RustDeskClientSharedSecret

`RustDeskClientSharedSecret` 是服务端与机台共享的 32 字节密钥，用于验证握手和派生临时凭据。

**首次启用前必须录入至少一个版本。**

操作步骤：

1. 进入 RustDesk 远程控制 → 命名管道密钥
2. 点击“新增版本”
3. 选择输入格式：
   - **Hex**：64 位小写十六进制字符串
   - **Base64**：32 字节的 base64 编码
   - **二进制文件**：直接选择包含 32 字节的文件
4. 填写审计备注（如 `首次初始化`）
5. 完成 OTP 验证后提交

服务端会自动激活新版本，并将旧版本置为失效；随后向所有已连接机台广播 `secret_outdated` 反向通知，触发机台重新拉取。

::: tip
建议定期轮换共享密钥。轮换时新旧版本不会同时生效，请确保所有机台已升级到能拉取新版本的客户端版本后再录入新版本。
:::

### 2. 管理可信主控端

可信主控端决定哪些 RustDesk ID 被允许连接机台。

进入 RustDesk 远程控制 → 可信主控端，可执行：

**新增主控端：**

| 字段 | 说明 |
|------|------|
| 主控端 ID | RustDesk 远程控制方的 RustDesk ID |
| HWID 哈希 | 可选。该主控端硬件指纹的 SHA-256（64 位小写十六进制）。留空则只匹配 ID |
| 标签 | 管理员可读说明，如 `运维笔记本` |
| 作用范围 | `global` 对所有机台生效；`machine:机台ID` 只对指定机台生效 |
| 启用 | 关闭后机台快照中不再包含该条 |
| 过期时间 | 过期后机台不再收到该条；留空表示永不过期 |
| 审计备注 | 写入审计日志的备注 |

**编辑 / 删除：** 点击列表中对应卡片的编辑或删除按钮。删除或编辑后，服务端会自动递增快照版本并通知相关机台刷新。

::: warning
可信主控端列表变更后，机台会在下次快照拉取周期（默认 5 分钟）内更新；如需立即生效，可在服务端触发反向通知或重启机台客户端。
:::

### 3. 查看上报记录与读取明文密码

机台成功上报 RustDesk ID 和密码后，管理员可在 **RustDesk 远程控制 → 上报记录** 查看。

**上报记录列表**仅显示：

- 机台 ID
- RustDesk ID
- 密码类型（临时/永久/预设/无密码）
- 上报时间
- 密钥版本

**读取明文密码：**

1. 点击目标机台的“读取明文”
2. 填写查询原因（如 `远程排查无法启动问题`）
3. 完成 OTP 验证
4. 弹窗显示明文密码，可复制

读取行为会写入审计日志，包含操作者、时间、IP、原因。

## 安全设计要点

| 环节 | 安全措施 |
|------|----------|
| 机台身份 | 所有机台请求均用机台 RSA 私钥签名，服务端校验证书指纹 |
| 密钥传输 | `RustDeskClientSharedSecret` 由服务端用机台 TPM 公钥 RSA-OAEP-SHA256 加密下发，不落机台磁盘 |
| 密码存储 | 服务端数据库中密码以明文存储；读取需 OTP step-up 并审计 |
| 主控端授权 | 机台本地 fail-closed：快照连续失败或过期后，拒绝所有 PeerApproval |
| 撤销通道 | 共享密钥或主控端列表变更时，服务端 best-effort 通知机台；机台周期刷新兜底 |

## 故障排查

| 现象 | 排查方向 |
|------|----------|
| 机台未出现在上报记录列表 | 检查机台 `EnableRustDeskBridge=true`；检查机台是否已审批；检查机台日志中 `RustDeskBridge` 相关错误 |
| 读取明文提示需要 OTP | 属于预期行为；在弹窗中输入当前验证器验证码 |
| 机台侧握手返回 `secret_outdated` | 管理端未录入 active secret，或 RustDesk 侧编译注入的 secretVersion 与服务端不一致 |
| 机台侧握手返回 `invalid_proof` | 密钥字节、nonce、时间窗或 HMAC 构造不匹配；核对协议文档 §5.2 |
| 机台侧 `rate_limited` | 60 秒内连续握手失败触发冷却；等待后重试 |
| 快照拉取连续失败 | 检查服务端策略签名密钥；检查机台时钟是否同步；连续失败 ≥3 次后机台会进入 fail-closed |
| 撤销通知未即时生效 | 反向通知仅 best-effort；等待下次快照周期（默认 5 分钟）或重启机台客户端 |
| 命名管道创建失败 | 确认客户端以管理员身份运行；检查是否有其他实例占用 `\\.\pipe\VHDMount.RustDeskBridge`；检查安全软件拦截 |

