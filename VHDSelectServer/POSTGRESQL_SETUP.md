# PostgreSQL安装和配置指南

## 📋 概述

本指南将帮助你在Windows系统上安装和配置PostgreSQL数据库，以支持VHDSelectServer的数据持久化功能。

## 🔧 安装PostgreSQL

### 方法一：官方安装程序（推荐）

1. **下载PostgreSQL**
   - 访问：https://www.postgresql.org/download/windows/
   - 点击"Download the installer"
   - 选择最新版本（推荐PostgreSQL 15或16）

2. **运行安装程序**
   - 以管理员身份运行下载的安装程序
   - 按照以下设置进行安装：

   ```
   安装组件：
   ✅ PostgreSQL Server
   ✅ pgAdmin 4
   ✅ Stack Builder
   ✅ Command Line Tools
   
   安装目录：C:\Program Files\PostgreSQL\16 (默认)
   数据目录：C:\Program Files\PostgreSQL\16\data (默认)
   
   超级用户密码：password
   端口：5432
   区域设置：[Default locale]
   ```

3. **完成安装**
   - 安装完成后，确保PostgreSQL服务已启动
   - 可以在"服务"中查看"postgresql-x64-16"服务状态

### 方法二：使用包管理器

如果你使用Chocolatey：
```bash
choco install postgresql
```

如果你使用Scoop：
```bash
scoop install postgresql
```

## 🗄️ 配置数据库

### 自动配置（推荐）

1. **运行配置脚本**
   ```bash
   cd VHDSelectServer
   setup_database.bat
   ```

2. **输入密码**
   - 脚本会提示输入PostgreSQL超级用户密码
   - 输入安装时设置的密码（默认：password）

### 手动配置

如果自动配置失败，可以手动执行以下步骤：

1. **打开命令提示符**
   ```bash
   # 设置PostgreSQL环境变量（如果需要）
   set PATH=%PATH%;C:\Program Files\PostgreSQL\16\bin
   ```

2. **连接到PostgreSQL**
   ```bash
   psql -U postgres -h localhost
   ```

3. **执行配置脚本**
   ```sql
   \i setup_postgresql.sql
   ```

## 🔍 验证安装

### 检查PostgreSQL服务

1. **检查服务状态**
   ```bash
   sc query postgresql-x64-16
   ```

2. **启动服务（如果未运行）**
   ```bash
   net start postgresql-x64-16
   ```

### 测试数据库连接

1. **使用psql连接**
   ```bash
   psql -U postgres -h localhost -d vhd_select
   ```

2. **查看表结构**
   ```sql
   \dt
   SELECT * FROM machines;
   ```

### 测试VHDSelectServer连接

1. **更新.env文件**
   确保`.env`文件中的配置正确：
   ```env
   DB_HOST=localhost
   DB_PORT=5432
   DB_NAME=vhd_select
   DB_USER=postgres
   DB_PASSWORD=password
   ```

2. **重启VHDSelectServer**
   ```bash
   node server.js
   ```

3. **检查连接状态**
   - 查看控制台输出，应该显示"数据库连接成功"
   - 访问Web UI：http://localhost:8080

## 🛠️ 故障排除

### 常见问题

1. **"psql不是内部或外部命令"**
   - 解决方案：将PostgreSQL的bin目录添加到系统PATH环境变量
   - 路径：`C:\Program Files\PostgreSQL\16\bin`

2. **连接被拒绝**
   - 检查PostgreSQL服务是否运行
   - 检查防火墙设置
   - 确认端口5432未被占用

3. **密码认证失败**
   - 确认超级用户密码正确
   - 检查pg_hba.conf配置文件

4. **数据库不存在**
   - 重新运行setup_postgresql.sql脚本
   - 手动创建数据库：`CREATE DATABASE vhd_select;`

### 重置数据库

如果需要重置数据库：

```sql
-- 连接到postgres数据库
\c postgres

-- 删除现有数据库
DROP DATABASE IF EXISTS vhd_select;

-- 重新运行配置脚本
\i setup_postgresql.sql
```

## 📊 数据库结构

### machines表结构

| 字段名 | 类型 | 说明 |
|--------|------|------|
| id | SERIAL | 主键，自增ID |
| machine_id | VARCHAR(255) | 机台唯一标识符 |
| protected | BOOLEAN | 保护状态 |
| vhd_keyword | VARCHAR(50) | VHD关键词 |
| created_at | TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | 更新时间 |

### 索引

- `idx_machines_machine_id`: machine_id字段索引
- `idx_machines_protected`: protected字段索引

## 🔐 安全建议

1. **修改默认密码**
   - 生产环境中应修改默认的postgres用户密码

2. **创建专用用户**
   ```sql
   CREATE USER vhd_user WITH PASSWORD 'secure_password';
   GRANT ALL PRIVILEGES ON DATABASE vhd_select TO vhd_user;
   ```

3. **配置防火墙**
   - 限制PostgreSQL端口的访问权限

4. **定期备份**
   ```bash
   pg_dump -U postgres vhd_select > backup.sql
   ```

## 📞 获取帮助

如果遇到问题，可以：

1. 查看PostgreSQL日志文件
2. 检查VHDSelectServer控制台输出
3. 使用pgAdmin 4图形界面管理数据库
4. 参考PostgreSQL官方文档：https://www.postgresql.org/docs/