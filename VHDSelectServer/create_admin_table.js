const { Pool } = require('pg');
const bcrypt = require('bcryptjs');

// 数据库配置 - 与database.js保持一致
const pool = new Pool({
    host: process.env.DB_HOST || 'localhost',
    port: parseInt(process.env.DB_PORT) || 5432,
    database: process.env.DB_NAME || 'vhd_select',
    user: process.env.DB_USER || 'postgres',
    password: process.env.DB_PASSWORD || 'password',
    ssl: process.env.DB_SSL === 'true' ? { rejectUnauthorized: false } : false,
});

async function createAdminTable() {
    try {
        console.log('连接数据库...');
        
        // 检查表是否存在
        const checkTableQuery = `
            SELECT EXISTS (
                SELECT FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = 'admin_settings'
            );
        `;
        
        const tableExists = await pool.query(checkTableQuery);
        console.log('admin_settings表是否存在:', tableExists.rows[0].exists);
        
        if (!tableExists.rows[0].exists) {
            console.log('创建admin_settings表...');
            
            // 创建表
            const createTableQuery = `
                CREATE TABLE admin_settings (
                    id SERIAL PRIMARY KEY,
                    setting_key VARCHAR(255) UNIQUE NOT NULL,
                    setting_value TEXT NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            `;
            
            await pool.query(createTableQuery);
            console.log('admin_settings表创建成功');
            
            // 创建更新时间触发器
            const createTriggerQuery = `
                CREATE OR REPLACE FUNCTION update_updated_at_column()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$ language 'plpgsql';

                CREATE TRIGGER update_admin_settings_updated_at 
                BEFORE UPDATE ON admin_settings 
                FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
            `;
            
            await pool.query(createTriggerQuery);
            console.log('更新时间触发器创建成功');
            
            // 插入默认密码
            const defaultPasswordHash = bcrypt.hashSync('admin123', 10);
            const insertDefaultQuery = `
                INSERT INTO admin_settings (setting_key, setting_value) 
                VALUES ('admin_password_hash', $1)
                ON CONFLICT (setting_key) DO NOTHING;
            `;
            
            await pool.query(insertDefaultQuery, [defaultPasswordHash]);
            console.log('默认管理员密码插入成功');
            
            // 创建索引
            const createIndexQuery = `
                CREATE INDEX IF NOT EXISTS idx_admin_settings_key ON admin_settings(setting_key);
            `;
            
            await pool.query(createIndexQuery);
            console.log('索引创建成功');
            
        } else {
            console.log('admin_settings表已存在，检查是否有默认密码...');
            
            // 检查是否有默认密码
            const checkPasswordQuery = `
                SELECT setting_value FROM admin_settings 
                WHERE setting_key = 'admin_password_hash';
            `;
            
            const passwordResult = await pool.query(checkPasswordQuery);
            
            if (passwordResult.rows.length === 0) {
                console.log('插入默认密码...');
                const defaultPasswordHash = bcrypt.hashSync('admin123', 10);
                const insertDefaultQuery = `
                    INSERT INTO admin_settings (setting_key, setting_value) 
                    VALUES ('admin_password_hash', $1);
                `;
                
                await pool.query(insertDefaultQuery, [defaultPasswordHash]);
                console.log('默认管理员密码插入成功');
            } else {
                console.log('管理员密码已存在');
            }
        }
        
        // 验证表结构
        const verifyQuery = `
            SELECT setting_key, LENGTH(setting_value) as value_length, created_at, updated_at 
            FROM admin_settings;
        `;
        
        const result = await pool.query(verifyQuery);
        console.log('admin_settings表内容:');
        console.table(result.rows);
        
        console.log('✅ admin_settings表设置完成');
        
    } catch (error) {
        console.error('❌ 创建admin_settings表失败:', error.message);
        console.error('详细错误:', error);
    } finally {
        await pool.end();
    }
}

createAdminTable();