const express = require('express');
const cors = require('cors');
const bodyParser = require('body-parser');
const path = require('path');
const fs = require('fs');

const app = express();
const PORT = process.env.PORT || 8080;

// 中间件
app.use(cors());
app.use(bodyParser.json());
app.use(bodyParser.urlencoded({ extended: true }));
app.use(express.static(path.join(__dirname, 'public')));

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

// API路由

// 获取当前VHD关键词
app.get('/api/boot-image-select', (req, res) => {
    console.log(`[${new Date().toISOString()}] API请求: GET /api/boot-image-select`);
    res.json({
        success: true,
        BootImageSelected: currentVhdKeyword,
        timestamp: new Date().toISOString()
    });
});

// 设置VHD关键词
app.post('/api/set-vhd', (req, res) => {
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

// 获取服务器状态
app.get('/api/status', (req, res) => {
    res.json({
        success: true,
        status: 'running',
        BootImageSelected: currentVhdKeyword,
        uptime: process.uptime(),
        timestamp: new Date().toISOString(),
        version: '1.0.0'
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
process.on('SIGINT', () => {
    console.log('\n正在关闭服务器...');
    saveConfig();
    process.exit(0);
});

process.on('SIGTERM', () => {
    console.log('\n正在关闭服务器...');
    saveConfig();
    process.exit(0);
});