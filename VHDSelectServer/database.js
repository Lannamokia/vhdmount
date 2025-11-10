const { Pool } = require('pg');
require('dotenv').config();

// 数据库配置
const dbConfig = {
    host: process.env.DB_HOST || 'localhost',
    port: parseInt(process.env.DB_PORT) || 5432,
    database: process.env.DB_NAME || 'vhd_select',
    user: process.env.DB_USER || 'postgres',
    password: process.env.DB_PASSWORD || 'password',
    max: parseInt(process.env.DB_MAX_CONNECTIONS) || 20, // 最大连接数
    idleTimeoutMillis: parseInt(process.env.DB_IDLE_TIMEOUT) || 30000, // 空闲连接超时时间
    connectionTimeoutMillis: parseInt(process.env.DB_CONNECTION_TIMEOUT) || 5000, // 连接超时时间
    ssl: process.env.DB_SSL === 'true' ? { rejectUnauthorized: false } : false,
};

// 创建PostgreSQL连接池
const pool = new Pool(dbConfig);

// 数据库操作类
class Database {
    constructor() {
        this.pool = pool;
        this.initializeDatabase();
    }

    // 初始化数据库连接
    async initializeDatabase() {
        const useEmbeddedDb = process.env.USE_EMBEDDED_DB === 'true';
        console.log(`数据库配置: ${useEmbeddedDb ? '内置数据库' : '外部数据库'}`);
        console.log(`连接地址: ${dbConfig.host}:${dbConfig.port}/${dbConfig.database}`);
        
        try {
            const client = await this.pool.connect();
            console.log('PostgreSQL数据库连接成功');
            
            // 测试查询
            const result = await client.query('SELECT NOW()');
            console.log('数据库时间:', result.rows[0].now);
            
            // 检查表是否存在
            const tableCheck = await client.query(`
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'machines'
                );
            `);
            
            if (tableCheck.rows[0].exists) {
                console.log('数据库表结构已存在');
                // 确保新增列存在（兼容升级）
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS evhd_password TEXT');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_id VARCHAR(64)');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS key_type VARCHAR(32)');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS pubkey_pem TEXT');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved BOOLEAN DEFAULT FALSE');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS approved_at TIMESTAMP');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked BOOLEAN DEFAULT FALSE');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS revoked_at TIMESTAMP');
                await client.query('ALTER TABLE machines ADD COLUMN IF NOT EXISTS last_seen TIMESTAMP');
                await client.query('CREATE INDEX IF NOT EXISTS idx_machines_key_id ON machines(key_id)');
                console.log('已确保machines表包含密钥与审批相关列');
            } else {
                console.log('警告: machines表不存在，请确保数据库已正确初始化');
            }
            
            client.release();
            this.isConnected = true;
        } catch (error) {
            console.error('数据库连接失败:', error.message);
            console.error('错误详情:', error);
            
            if (useEmbeddedDb) {
                console.error('内置数据库连接失败，请检查Docker配置');
            } else {
                console.error('外部数据库连接失败，请检查数据库服务器状态和连接配置');
            }
            
            this.isConnected = false;
            throw error;
        }
    }

    // 获取机台信息
    async getMachine(machineId) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'SELECT * FROM machines WHERE machine_id = $1',
                [machineId]
            );
            client.release();
            
            return result.rows[0] || null;
        } catch (error) {
            console.error('获取机台信息失败:', error.message);
            return null;
        }
    }

    // 创建或更新机台信息
    async upsertMachine(machineId, isProtected = false, vhdKeyword = 'SDEZ') {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                INSERT INTO machines (machine_id, protected, vhd_keyword)
                VALUES ($1, $2, $3)
                ON CONFLICT (machine_id)
                DO UPDATE SET
                    protected = EXCLUDED.protected,
                    vhd_keyword = EXCLUDED.vhd_keyword,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING *
            `, [machineId, isProtected, vhdKeyword]);
            client.release();
            
            return result.rows[0];
        } catch (error) {
            console.error('更新机台信息失败:', error.message);
            return null;
        }
    }

    // 更新机台保护状态
    async updateMachineProtection(machineId, isProtected) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines 
                SET protected = $2, updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING *
            `, [machineId, isProtected]);
            client.release();
            
            return result.rows[0] || null;
        } catch (error) {
            console.error('更新机台保护状态失败:', error.message);
            return null;
        }
    }

    // 更新机台VHD关键词
    async updateMachineVhdKeyword(machineId, vhdKeyword) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines 
                SET vhd_keyword = $2, updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING *
            `, [machineId, vhdKeyword]);
            client.release();
            
            return result.rows[0] || null;
        } catch (error) {
            console.error('更新机台VHD关键词失败:', error.message);
            return null;
        }
    }

    // 获取机台EVHD密码
    async getMachineEvhdPassword(machineId) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'SELECT evhd_password FROM machines WHERE machine_id = $1',
                [machineId]
            );
            client.release();
            return result.rows[0]?.evhd_password || null;
        } catch (error) {
            console.error('获取机台EVHD密码失败:', error.message);
            return null;
        }
    }

    // 更新机台EVHD密码
    async updateMachineEvhdPassword(machineId, evhdPassword) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines 
                SET evhd_password = $2, updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING *
            `, [machineId, evhdPassword]);
            client.release();
            return result.rows[0] || null;
        } catch (error) {
            console.error('更新机台EVHD密码失败:', error.message);
            return null;
        }
    }

    // 获取所有机台信息
    async getAllMachines() {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'SELECT * FROM machines ORDER BY machine_id'
            );
            client.release();
            
            return result.rows;
        } catch (error) {
            console.error('获取所有机台信息失败:', error.message);
            return [];
        }
    }

    // 删除机台
    async deleteMachine(machineId) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'DELETE FROM machines WHERE machine_id = $1 RETURNING *',
                [machineId]
            );
            client.release();
            
            return result.rows[0] || null;
        } catch (error) {
            console.error('删除机台失败:', error.message);
            return null;
        }
    }

    // 更新最近在线时间
    async updateMachineLastSeen(machineId) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines
                SET last_seen = CURRENT_TIMESTAMP,
                    updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING last_seen
            `, [machineId]);
            client.release();
            return result.rows[0]?.last_seen || null;
        } catch (error) {
            console.error('更新机台最近在线时间失败:', error.message);
            return null;
        }
    }

    // 更新机台密钥（注册/替换），重置审批与吊销状态
    async updateMachineKey(machineId, { keyId, keyType, pubkeyPem }) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                INSERT INTO machines (machine_id, key_id, key_type, pubkey_pem, approved, revoked, updated_at)
                VALUES ($1, $2, $3, $4, FALSE, FALSE, CURRENT_TIMESTAMP)
                ON CONFLICT (machine_id)
                DO UPDATE SET
                    key_id = EXCLUDED.key_id,
                    key_type = EXCLUDED.key_type,
                    pubkey_pem = EXCLUDED.pubkey_pem,
                    approved = CASE WHEN machines.pubkey_pem IS DISTINCT FROM EXCLUDED.pubkey_pem THEN FALSE ELSE machines.approved END,
                    approved_at = CASE WHEN machines.pubkey_pem IS DISTINCT FROM EXCLUDED.pubkey_pem THEN NULL ELSE machines.approved_at END,
                    revoked = CASE WHEN machines.pubkey_pem IS DISTINCT FROM EXCLUDED.pubkey_pem THEN FALSE ELSE machines.revoked END,
                    revoked_at = CASE WHEN machines.pubkey_pem IS DISTINCT FROM EXCLUDED.pubkey_pem THEN NULL ELSE machines.revoked_at END,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING *
            `, [machineId, keyId, keyType, pubkeyPem]);
            client.release();
            return result.rows[0] || null;
        } catch (error) {
            console.error('更新机台密钥失败:', error.message);
            return null;
        }
    }

    // 审批机台密钥
    async approveMachine(machineId, approved) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines
                SET approved = $2,
                    approved_at = CASE WHEN $2 THEN CURRENT_TIMESTAMP ELSE NULL END,
                    updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING *
            `, [machineId, !!approved]);
            client.release();
            return result.rows[0] || null;
        } catch (error) {
            console.error('审批机台失败:', error.message);
            return null;
        }
    }

    // 重置机台注册状态：删除相关密钥并重置审批/吊销为未审批
    async revokeMachineKey(machineId) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                UPDATE machines
                SET key_id = NULL,
                    key_type = NULL,
                    pubkey_pem = NULL,
                    approved = FALSE,
                    approved_at = NULL,
                    revoked = FALSE,
                    revoked_at = NULL,
                    updated_at = CURRENT_TIMESTAMP
                WHERE machine_id = $1
                RETURNING *
            `, [machineId]);
            client.release();
            return result.rows[0] || null;
        } catch (error) {
            console.error('重置机台注册状态失败:', error.message);
            return null;
        }
    }

    // 获取管理员密码哈希
    async getAdminPasswordHash() {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'SELECT setting_value FROM admin_settings WHERE setting_key = $1',
                ['admin_password_hash']
            );
            client.release();
            
            return result.rows[0]?.setting_value || null;
        } catch (error) {
            console.error('获取管理员密码失败:', error.message);
            return null;
        }
    }

    // 更新管理员密码哈希
    async updateAdminPasswordHash(passwordHash) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                INSERT INTO admin_settings (setting_key, setting_value)
                VALUES ($1, $2)
                ON CONFLICT (setting_key)
                DO UPDATE SET
                    setting_value = EXCLUDED.setting_value,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING *
            `, ['admin_password_hash', passwordHash]);
            client.release();
            
            return result.rows[0];
        } catch (error) {
            console.error('更新管理员密码失败:', error.message);
            throw error;
        }
    }

    // 获取设置值
    async getSetting(key) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(
                'SELECT setting_value FROM admin_settings WHERE setting_key = $1',
                [key]
            );
            client.release();
            
            return result.rows[0]?.setting_value || null;
        } catch (error) {
            console.error(`获取设置 ${key} 失败:`, error.message);
            return null;
        }
    }

    // 更新设置值
    async updateSetting(key, value) {
        try {
            const client = await this.pool.connect();
            const result = await client.query(`
                INSERT INTO admin_settings (setting_key, setting_value)
                VALUES ($1, $2)
                ON CONFLICT (setting_key)
                DO UPDATE SET
                    setting_value = EXCLUDED.setting_value,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING *
            `, [key, value]);
            client.release();
            
            return result.rows[0];
        } catch (error) {
            console.error(`更新设置 ${key} 失败:`, error.message);
            throw error;
        }
    }

    // 关闭数据库连接池
    async close() {
        try {
            await this.pool.end();
            console.log('数据库连接池已关闭');
        } catch (error) {
            console.error('关闭数据库连接池失败:', error.message);
        }
    }
}

module.exports = new Database();