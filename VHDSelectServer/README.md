# VHDSelectServer

VHDSelectServer 是 VHD 挂载方案的管理服务，负责提供机台管理、EVHD 密码封装下发、可信证书注册、审计日志与初始化流程。当前版本已废弃旧 Web 管理前端，推荐通过 Flutter 管理客户端或受控 API 进行管理。

## 功能概览

- 锁文件初始化流程：首次部署必须先完成初始化，系统不再内置默认管理员密码。
- Session 登录与 OTP 二次验证：高敏感操作需要额外 OTP 验证。
- OTP 二次验证通过后仅保留 60 秒高敏感操作窗口，超时后需要重新验证。
- 预配置证书签名注册：机台公钥注册必须由可信注册证书签名。
- EVHD 密码下发：公开接口提供 RSA-OAEP-SHA1 封装信封，明文读取仅限管理员排障并要求 OTP。
- 审计日志：关键安全操作记录到 JSONL 审计文件。
- PostgreSQL 持久化：支持 Docker 内置数据库或外部数据库。

## 系统要求

- Node.js 18+
- Docker 20.10+ / Compose 2+（可选）
- PostgreSQL 15+（外部数据库模式）

## 快速开始

### Docker 部署

内置数据库模式：

```bash
docker-compose up -d
docker-compose logs -f
```

如果需要把服务配置持久化到宿主机目录，请像 PostgreSQL 一样映射父目录到 `/app/config`，让实际配置保持在子目录 `/app/config/data`。例如：

```yaml
volumes:
	- ./config:/app/config
environment:
	- CONFIG_ROOT_DIR=/app/config
	- CONFIG_PATH=/app/config/data
```

原因：Docker Desktop/部分 bind mount 文件系统不会稳定保留挂载根目录上的 Linux 所有权与权限。把真正写入的配置文件放到子目录，可以在容器启动时由 root 创建并修复这个子目录，再交给 `nodejs` 用户写入，和内置 PostgreSQL 使用 `pgdata` 子目录的思路一致。首次升级到这个布局时，入口脚本会自动把旧版直接放在 `/app/config` 根目录的配置文件迁移到子目录。

如果需要把 PostgreSQL 数据持久化到宿主机目录，请映射父目录到 `/var/lib/postgresql/data`，让实际 cluster 保持在容器内的 `pgdata` 子目录。例如：

```yaml
volumes:
	- ./postgres-data:/var/lib/postgresql/data
environment:
	- POSTGRES_DATA_DIR=/var/lib/postgresql/data/pgdata
```

原因：Docker Desktop/部分 bind mount 文件系统不会正确保留挂载根目录上的 Linux 所有权和 `0700` 权限，而 PostgreSQL 要求真实数据目录必须由 `postgres` 拥有并具备严格权限。把实际 PGDATA 放到子目录可避免这类挂载根目录权限冲突。

外部数据库模式：

```bash
docker-compose -f docker-compose.external-db.yml up -d
```

### Node.js 直接运行

```bash
npm install
npm run migrate
npm start
```

如果服务目录中已经存在初始化完成的 `server-security.json`，`npm run migrate` 会优先读取其中保存的数据库配置；否则使用当前环境变量中的 `DB_*` 配置。应用启动时也会自动执行同一套 schema migrations。

默认监听地址：`http://localhost:8080`

访问根路径 `/` 时，服务端会返回一个轻量跳转提示页，用来引导用户下载 Flutter 管理客户端，并根据当前浏览器访问入口提示应填写的服务器地址。

## 首次初始化

服务启动后，先检查初始化状态：

```http
GET /api/init/status
```

若未初始化，按以下顺序执行：

1. `POST /api/init/prepare`
2. 保存返回的 OTP 秘钥、初始化令牌和建议配置
3. `POST /api/init/complete`

初始化完成后系统会生成安全配置文件与锁文件，后续启动将拒绝再次初始化。

推荐直接使用仓库中的 Flutter 管理端完成该流程。

## 管理方式

当前受支持的管理入口：

- 仓库根目录下的 `vhd_mount_admin_flutter` Windows 客户端
- 受控脚本或后端系统调用 REST API
- 服务根路径 `/` 的只读跳转提示页，用于下载指引与地址填写提示

旧版可操作 Web 管理页已移除，根路径仅保留下载与连接说明，不再提供 `public/` 管理前端资源。

## 关键 API

### 初始化

- `GET /api/init/status`
- `POST /api/init/prepare`
- `POST /api/init/complete`

### 认证

- `POST /api/auth/login`
- `GET /api/auth/check`
- `POST /api/auth/logout`
- `POST /api/auth/change-password`
- `POST /api/auth/otp/verify`
- `GET /api/auth/otp/status`

### 机台与密码

- `GET /api/boot-image-select?machineId=...`
- `POST /api/set-vhd`
- `GET /api/protect?machineId=...`
- `POST /api/protect`
- `GET /api/machines`
- `POST /api/machines`
- `DELETE /api/machines/:machineId`
- `POST /api/machines/:machineId/vhd`
- `POST /api/machines/:machineId/evhd-password`
- `POST /api/machines/:machineId/keys`
- `POST /api/machines/:machineId/approve`
- `POST /api/machines/:machineId/revoke`
- `GET /api/evhd-envelope?machineId=...`
- `GET /api/evhd-password/plain?machineId=...&reason=...`

### 可信注册证书与审计

- `GET /api/security/trusted-certificates`
- `POST /api/security/trusted-certificates`
- `DELETE /api/security/trusted-certificates/:fingerprint`
- `GET /api/audit`

## 安全模型

- 无默认管理员密码。
- 无固定 Session Secret，初始化时生成并持久化。
- 带 `Origin` 头的浏览器请求必须命中初始化时配置的 `allowedOrigins` 白名单。
- Session Cookie 使用 `SameSite=Strict`，并按请求协议自动决定是否加 `Secure`。
- 明文 EVHD 查询必须先登录并完成 OTP 二次验证。
- 机台注册请求必须携带可信注册证书、签名、时间戳与 nonce。
- 注册接口执行防重放校验。
- 审计日志覆盖初始化、认证、证书管理、敏感读取等操作。
- 审计日志采用有界轮转，默认保留最近 5 个文件，避免长期运行后无限增长。
- **生产环境强烈建议通过反向代理启用 TLS/HTTPS**，管理端的密码、OTP、EVHD 等敏感数据在公网传输中需要 TLS 保护。机台侧通信已采用 AES-256-CTR + RSA-TPM 端到端加密，不依赖 TLS。

## 环境变量

应用配置：

- `PORT`：服务端口，默认 `8080`
- `NODE_ENV`：运行环境，默认 `production`
- `MACHINE_REGISTRATION_RATE_LIMIT_MAX`：机台公钥注册接口在 10 分钟窗口内的单机限流阈值，默认 `20`
- `AUDIT_LOG_MAX_BYTES`：单个审计日志文件最大字节数，默认 `5242880`（5 MiB）
- `AUDIT_LOG_MAX_FILES`：审计日志最多保留的文件数（含当前文件），默认 `5`

数据库配置：

- `USE_EMBEDDED_DB`：是否使用内置数据库，默认 `true`
- `DB_HOST`
- `DB_PORT`
- `DB_NAME`
- `DB_USER`
- `DB_PASSWORD`
- `DB_MAX_CONNECTIONS`
- `DB_IDLE_TIMEOUT`
- `DB_CONNECTION_TIMEOUT`
- `DB_SSL`

生产环境应显式设置数据库凭据，不要依赖示例值。**使用外部数据库时，务必设置高强度密码，不要使用低强度或默认口令。**

## 测试

运行服务端自动化测试：

```bash
npm test
```

## 数据库迁移

- 迁移文件位于 `migrations/`，按 `001_xxx.sql`、`002_xxx.sql` 这种连续版本号管理。
- 应用启动时会自动执行 schema_version 迁移；也可以手动运行 `npm run migrate`。
- `init-db.sql` 与 `setup_postgresql.sql` 现在只作为迁移包装脚本，不再各自维护一份独立的完整表结构快照。

当前测试覆盖：

- 初始化流程
- Origin 白名单与未授权跨域拒绝
- OTP 验证
- 可信证书签名注册
- nonce 防重放
- `machineId` 参数校验

容器健康检查使用 `GET /api/health`。

## 目录结构

```text
VHDSelectServer/
├── server.js
├── database.js
├── schemaMigrations.js
├── migrate.js
├── migrations/
│   ├── 001_initial_schema.sql
│   ├── 002_machine_security_columns.sql
│   └── 003_machine_log_schema.sql
├── securityStore.js
├── registrationAuth.js
├── auditLog.js
├── validators.js
├── test/
│   └── server.test.js
├── Dockerfile
├── docker-compose.yml
├── docker-compose.external-db.yml
├── docker-entrypoint.sh
├── init-db.sql
├── setup_postgresql.sql
└── README.md
```

## 故障排查

- 初始化后无法再次进入初始化流程：检查配置目录中的锁文件是否已生成，这是预期行为。
- 注册验签失败：确认客户端使用的是受信注册证书，且签名载荷经过规范化处理。
- 明文 EVHD 查询返回未授权：确认已登录且已完成 OTP 验证，并提供审计原因参数。
- Docker 外部数据库连接失败：核对 `DB_*` 环境变量并检查 PostgreSQL 网络连通性。

## 说明

该服务仍会对外提供兼容的 `GET /api/boot-image-select` 能力，以保持现有挂载流程可继续工作；但管理面已切换为更严格的初始化、认证和注册模型。
