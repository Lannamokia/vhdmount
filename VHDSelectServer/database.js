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