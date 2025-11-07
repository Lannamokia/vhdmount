const express = require('express');
const cors = require('cors');
const bodyParser = require('body-parser');
const path = require('path');
const fs = require('fs');
const session = require('express-session');
const bcrypt = require('bcryptjs');
const crypto = require('crypto');
require('dotenv').config();

const database = require('./database');

const app = express();
const PORT = process.env.PORT || 8080;

// 管理员密码配置 (默认密码: admin123) - 现在从数据库获取
const DEFAULT_ADMIN_PASSWORD_HASH = process.env.ADMIN_PASSWORD_HASH || bcrypt.hashSync('admin123', 10);

// 会话配置
app.use(session({
    secret: process.env.SESSION_SECRET || 'vhd-select-server-secret-key-2024',
    resave: false,
    saveUninitialized: false,
    cookie: {
        secure: false, // 在生产环境中应该设置为true (需要HTTPS)
        httpOnly: true,
        maxAge: 24 * 60 * 60 * 1000 // 24小时
    }
}));

// 中间件
app.use(cors({
    origin: true,
    credentials: true // 允许发送cookies
}));
app.use(bodyParser.json());
app.use(bodyParser.urlencoded({ extended: true }));

// VHD关键词存储
let currentVhdKeyword = 'SDEZ'; // 默认值

// 配置文件路径 - 支持Docker环境
const configDir = process.env.CONFIG_PATH || __dirname;
const configFile = path.join(configDir, 'vhd-config.json');

// 确保配置目录存在
if (!fs.existsSync(configDir)) {
    fs.mkdirSync(configDir, { recursive: true });
}

// 加载配置
function loadConfig() {
    try {
        if (fs.existsSync(configFile)) {
            const config = JSON.parse(fs.readFileSync(configFile, 'utf8'));
            currentVhdKeyword = config.vhdKeyword || 'SDEZ';
        }
    } catch (error) {
        console.log('配置文件加载失败，使用默认值:', error.message);
    }
}

// 保存配置
function saveConfig() {
    try {
        const config = { vhdKeyword: currentVhdKeyword };
        fs.writeFileSync(configFile, JSON.stringify(config, null, 2));
    } catch (error) {
        console.error('配置文件保存失败:', error.message);
    }
}

// 使用secret进行AES-256-CBC加密
function encryptWithSecret(secret, plaintext) {
    try {
        const key = crypto.createHash('sha256').update(secret).digest();
        const iv = crypto.createHash('sha256').update(secret + '_iv').digest().subarray(0, 16);
        const cipher = crypto.createCipheriv('aes-256-cbc', key, iv);
        let encrypted = cipher.update(plaintext, 'utf8', 'base64');
        encrypted += cipher.final('base64');
        return encrypted;
    } catch (err) {
        console.error('EVHD密码加密失败:', err.message);
        return '';
    }
}

// 认证中间件
function requireAuth(req, res, next) {
    if (req.session && req.session.isAuthenticated) {
        return next();
    } else {
        return res.status(401).json({ 
            success: false, 
            message: '需要登录',
            requireAuth: true 
        });
    }
}

// 静态文件服务 (登录页面不需要认证)
app.use(express.static(path.join(__dirname, 'public')));

// 认证API路由
app.post('/api/auth/login', async (req, res) => {
    try {
        const { password } = req.body;
        
        console.log(`[${new Date().toISOString()}] 登录尝试`);
        
        if (!password) {
            return res.status(400).json({
                success: false,
                message: '请输入密码'
            });
        }
        
        // 从数据库获取密码哈希
        let adminPasswordHash = await database.getAdminPasswordHash();
        
        // 如果数据库中没有密码，使用默认密码
        if (!adminPasswordHash) {
            adminPasswordHash = DEFAULT_ADMIN_PASSWORD_HASH;
            // 将默认密码保存到数据库
            try {
                await database.updateAdminPasswordHash(adminPasswordHash);
                console.log('默认管理员密码已保存到数据库');
            } catch (error) {
                console.error('保存默认密码到数据库失败:', error.message);
            }
        }
        
        // 验证密码
        const isValidPassword = await bcrypt.compare(password, adminPasswordHash);
        
        if (isValidPassword) {
            req.session.isAuthenticated = true;
            console.log(`[${new Date().toISOString()}] 登录成功`);
            res.json({
                success: true,
                message: '登录成功'
            });
        } else {
            console.log(`[${new Date().toISOString()}] 登录失败: 密码错误`);
            res.status(401).json({
                success: false,
                message: '密码错误'
            });
        }
    } catch (error) {
        console.error('登录错误:', error);
        res.status(500).json({
            success: false,
            message: '服务器错误'
        });
    }
});

app.post('/api/auth/logout', (req, res) => {
    req.session.destroy((err) => {
        if (err) {
            console.error('登出错误:', err);
            return res.status(500).json({
                success: false,
                message: '登出失败'
            });
        }
        res.json({
            success: true,
            message: '已登出'
        });
    });
});

app.get('/api/auth/check', (req, res) => {
    res.json({
        isAuthenticated: !!(req.session && req.session.isAuthenticated)
    });
});

// 修改管理员密码
app.post('/api/auth/change-password', requireAuth, async (req, res) => {
    try {
        const { currentPassword, newPassword, confirmPassword } = req.body;
        
        console.log(`[${new Date().toISOString()}] 密码修改请求`);
        
        // 验证输入
        if (!currentPassword || !newPassword || !confirmPassword) {
            return res.status(400).json({
                success: false,
                message: '请填写所有密码字段'
            });
        }
        
        if (newPassword !== confirmPassword) {
            return res.status(400).json({
                success: false,
                message: '新密码和确认密码不匹配'
            });
        }
        
        if (newPassword.length < 6) {
            return res.status(400).json({
                success: false,
                message: '新密码长度至少为6位'
            });
        }
        
        // 获取当前密码哈希
        const currentPasswordHash = await database.getAdminPasswordHash();
        if (!currentPasswordHash) {
            return res.status(500).json({
                success: false,
                message: '无法获取当前密码信息'
            });
        }
        
        // 验证当前密码
        const isCurrentPasswordValid = await bcrypt.compare(currentPassword, currentPasswordHash);
        if (!isCurrentPasswordValid) {
            console.log(`[${new Date().toISOString()}] 密码修改失败: 当前密码错误`);
            return res.status(401).json({
                success: false,
                message: '当前密码错误'
            });
        }
        
        // 生成新密码哈希
        const newPasswordHash = await bcrypt.hash(newPassword, 10);
        
        // 更新数据库中的密码
        await database.updateAdminPasswordHash(newPasswordHash);
        
        console.log(`[${new Date().toISOString()}] 管理员密码修改成功`);
        
        res.json({
            success: true,
            message: '密码修改成功'
        });
        
    } catch (error) {
        console.error('密码修改错误:', error);
        res.status(500).json({
            success: false,
            message: '服务器错误，密码修改失败'
        });
    }
});

// API路由

// 获取当前VHD关键词 - 需要Machine ID验证
app.get('/api/boot-image-select', async (req, res) => {
    const machineId = req.query.machineId;
    
    console.log(`[${new Date().toISOString()}] API请求: GET /api/boot-image-select, Machine ID: ${machineId}`);
    
    if (!machineId) {
        return res.status(400).json({
            success: false,
            error: 'Machine ID是必需的'
        });
    }
    
    try {
        // 从数据库获取机台信息
        let machine = await database.getMachine(machineId);
        
        // 如果机台不存在，创建新的机台记录
        if (!machine) {
            machine = await database.upsertMachine(machineId, false, currentVhdKeyword);
            console.log(`[${new Date().toISOString()}] 创建新机台: ${machineId}`);
        }
        
        res.json({
            success: true,
            BootImageSelected: machine ? machine.vhd_keyword : currentVhdKeyword,
            machineId: machineId,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('获取VHD关键词失败:', error.message);
        // 降级到内存存储
        res.json({
            success: true,
            BootImageSelected: currentVhdKeyword,
            machineId: machineId,
            timestamp: new Date().toISOString()
        });
    }
});

// 设置VHD关键词 (需要认证)
app.post('/api/set-vhd', requireAuth, (req, res) => {
    const { BootImageSelected } = req.body;
    
    if (!BootImageSelected || typeof BootImageSelected !== 'string') {
        return res.status(400).json({
            success: false,
            error: 'VHD关键词不能为空且必须是字符串'
        });
    }
    
    // 验证关键词格式（可选：添加更严格的验证）
    const trimmedKeyword = BootImageSelected.trim().toUpperCase();
    if (trimmedKeyword.length === 0) {
        return res.status(400).json({
            success: false,
            error: 'VHD关键词不能为空'
        });
    }
    
    currentVhdKeyword = trimmedKeyword;
    saveConfig();
    
    console.log(`[${new Date().toISOString()}] VHD关键词已更新为: ${currentVhdKeyword}`);
    
    res.json({
        success: true,
        BootImageSelected: currentVhdKeyword,
        message: 'VHD关键词更新成功'
    });
});

// 保护状态检查端点 - 需要Machine ID验证
app.get('/api/protect', async (req, res) => {
    const machineId = req.query.machineId;
    
    console.log(`[${new Date().toISOString()}] API请求: GET /api/protect, Machine ID: ${machineId}`);
    
    if (!machineId) {
        return res.status(400).json({
            success: false,
            error: 'Machine ID是必需的'
        });
    }
    
    try {
        // 从数据库获取机台信息
        const machine = await database.getMachine(machineId);
        
        // 如果机台不存在，直接返回错误，不自动创建
        if (!machine) {
            return res.status(404).json({
                success: false,
                error: '机台不存在',
                machineId
            });
        }
        
        res.json({
            success: true,
            protected: machine.protected,
            machineId,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('获取保护状态失败:', error.message);
        return res.status(500).json({
            success: false,
            error: '获取保护状态失败'
        });
    }
});

// 设置机台保护状态
app.post('/api/protect', requireAuth, async (req, res) => {
    const { machineId, protected } = req.body;
    
    console.log(`[${new Date().toISOString()}] API请求: POST /api/protect, Machine ID: ${machineId}, Protected: ${protected}`);
    
    if (!machineId) {
        return res.status(400).json({
            success: false,
            error: 'Machine ID是必需的'
        });
    }
    
    if (typeof protected !== 'boolean') {
        return res.status(400).json({
            success: false,
            error: 'protected状态必须是布尔值'
        });
    }
    
    try {
        // 更新机台保护状态
        const machine = await database.updateMachineProtection(machineId, protected);
        
        if (!machine) {
            // 如果机台不存在，返回错误，不自动创建
            return res.status(404).json({
                success: false,
                error: '机台不存在',
                machineId
            });
        }
        
        res.json({
            success: true,
            protected: machine.protected,
            machineId,
            message: '机台保护状态已更新'
        });
    } catch (error) {
        console.error('设置保护状态失败:', error.message);
        res.status(500).json({
            success: false,
            error: '设置保护状态失败'
        });
    }
});

// 获取所有机台信息
app.get('/api/machines', requireAuth, async (req, res) => {
    console.log(`[${new Date().toISOString()}] API请求: GET /api/machines`);
    
    try {
        const machines = await database.getAllMachines();
        res.json({
            success: true,
            machines: machines,
            count: machines.length,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('获取机台列表失败:', error.message);
        res.status(500).json({
            success: false,
            error: '获取机台列表失败'
        });
    }
});

// 获取机台EVHD密码（公开，客户端使用）
app.get('/api/evhd-password', async (req, res) => {
    const machineId = req.query.machineId;
    const secret = req.query.secret;
    console.log(`[${new Date().toISOString()}] API请求: GET /api/evhd-password, Machine ID: ${machineId}`);
    if (!machineId) {
        return res.status(400).json({ success: false, error: 'Machine ID是必需的' });
    }
    if (!secret || typeof secret !== 'string') {
        return res.status(400).json({ success: false, error: 'secret是必需的' });
    }
    try {
        // 确保机台存在
        let machine = await database.getMachine(machineId);
        if (!machine) {
            machine = await database.upsertMachine(machineId, false, currentVhdKeyword);
            console.log(`[${new Date().toISOString()}] 创建新机台: ${machineId}`);
        }
        const evhdPassword = await database.getMachineEvhdPassword(machineId);
        const cipher = evhdPassword ? encryptWithSecret(secret, evhdPassword) : '';
        res.json({ success: true, machineId, evhdPassword: cipher });
    } catch (error) {
        console.error('获取EVHD密码失败:', error.message);
        res.status(500).json({ success: false, error: '获取EVHD密码失败' });
    }
});

// 获取机台EVHD密码（明文，需登录）
app.get('/api/evhd-password/plain', requireAuth, async (req, res) => {
    const machineId = req.query.machineId;
    console.log(`[${new Date().toISOString()}] API请求: GET /api/evhd-password/plain, Machine ID: ${machineId}`);
    if (!machineId) {
        return res.status(400).json({ success: false, error: 'Machine ID是必需的' });
    }
    try {
        // 查询机台，不自动创建，不存在则返回404
        const machine = await database.getMachine(machineId);
        if (!machine) {
            return res.status(404).json({ success: false, error: '机台不存在', machineId });
        }
        const evhdPassword = await database.getMachineEvhdPassword(machineId);
        // 若未设置密码，返回空串，同时 success:true 以便前端统一处理
        return res.json({ success: true, machineId, evhdPassword: evhdPassword || '' });
    } catch (error) {
        console.error('获取明文EVHD密码失败:', error.message);
        return res.status(500).json({ success: false, error: '获取EVHD密码失败' });
    }
});

// 设置特定机台的VHD关键词
app.post('/api/machines/:machineId/vhd', requireAuth, async (req, res) => {
    const { machineId } = req.params;
    const { vhdKeyword } = req.body;
    
    console.log(`[${new Date().toISOString()}] API请求: POST /api/machines/${machineId}/vhd, VHD: ${vhdKeyword}`);
    
    if (!vhdKeyword || typeof vhdKeyword !== 'string') {
        return res.status(400).json({
            success: false,
            error: 'VHD关键词不能为空且必须是字符串'
        });
    }
    
    const trimmedKeyword = vhdKeyword.trim().toUpperCase();
    if (trimmedKeyword.length === 0) {
        return res.status(400).json({
            success: false,
            error: 'VHD关键词不能为空'
        });
    }
    
    try {
        // 更新机台VHD关键词
        let machine = await database.updateMachineVhdKeyword(machineId, trimmedKeyword);
        
        if (!machine) {
            // 如果机台不存在，创建新的机台记录
            machine = await database.upsertMachine(machineId, false, trimmedKeyword);
        }
        
        res.json({
            success: true,
            machineId: machineId,
            vhdKeyword: machine.vhd_keyword,
            message: '机台VHD关键词已更新'
        });
    } catch (error) {
        console.error('设置机台VHD关键词失败:', error.message);
        res.status(500).json({
            success: false,
            error: '设置机台VHD关键词失败'
        });
    }
});

// 设置特定机台的EVHD密码（需登录）
app.post('/api/machines/:machineId/evhd-password', requireAuth, async (req, res) => {
    const { machineId } = req.params;
    const { evhdPassword } = req.body;
    console.log(`[${new Date().toISOString()}] API请求: POST /api/machines/${machineId}/evhd-password`);
    if (typeof evhdPassword !== 'string') {
        return res.status(400).json({ success: false, error: 'EVHD密码必须是字符串' });
    }
    try {
        let machine = await database.updateMachineEvhdPassword(machineId, evhdPassword);
        if (!machine) {
            // 如果机台不存在，创建后更新
            await database.upsertMachine(machineId, false, currentVhdKeyword);
            machine = await database.updateMachineEvhdPassword(machineId, evhdPassword);
        }
        res.json({ success: true, machineId, message: 'EVHD密码已更新' });
    } catch (error) {
        console.error('设置机台EVHD密码失败:', error.message);
        res.status(500).json({ success: false, error: '设置机台EVHD密码失败' });
    }
});

// 删除机台
app.delete('/api/machines/:machineId', requireAuth, async (req, res) => {
    const { machineId } = req.params;
    
    console.log(`[${new Date().toISOString()}] API请求: DELETE /api/machines/${machineId}`);
    
    try {
        const deletedMachine = await database.deleteMachine(machineId);
        
        if (!deletedMachine) {
            return res.status(404).json({
                success: false,
                error: '机台不存在'
            });
        }
        
        res.json({
            success: true,
            machineId: machineId,
            message: '机台已删除'
        });
    } catch (error) {
        console.error('删除机台失败:', error.message);
        res.status(500).json({
            success: false,
            error: '删除机台失败'
        });
    }
});

// 获取服务器状态
app.get('/api/status', (req, res) => {
    res.json({
        success: true,
        status: 'running',
        BootImageSelected: currentVhdKeyword,
        uptime: process.uptime(),
        timestamp: new Date().toISOString(),
        version: '1.2.1'
    });
});

// 主页路由
app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

// 404处理
app.use((req, res) => {
    res.status(404).json({
        success: false,
        error: '页面未找到'
    });
});

// 错误处理
app.use((err, req, res, next) => {
    console.error('服务器错误:', err);
    res.status(500).json({
        success: false,
        error: '服务器内部错误'
    });
});

// 启动服务器
loadConfig();

app.listen(PORT, () => {
    console.log('='.repeat(50));
    console.log('🚀 VHD选择服务器已启动');
    console.log(`📡 服务器地址: http://localhost:${PORT}`);
    console.log(`🔧 API地址: http://localhost:${PORT}/api/boot-image-select`);
    console.log(`📊 状态页面: http://localhost:${PORT}/api/status`);
    console.log(`🎯 当前VHD关键词: ${currentVhdKeyword}`);
    console.log('='.repeat(50));
});

// 优雅关闭
process.on('SIGINT', async () => {
    console.log('\n正在关闭服务器...');
    saveConfig();
    await database.close();
    process.exit(0);
});

process.on('SIGTERM', async () => {
    console.log('\n正在关闭服务器...');
    saveConfig();
    await database.close();
    process.exit(0);
});