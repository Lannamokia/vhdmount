require('dotenv').config();

const crypto = require('crypto');
const express = require('express');
const fs = require('fs');
const helmet = require('helmet');
const path = require('path');
const rateLimit = require('express-rate-limit');
const session = require('express-session');

const { AuditLog } = require('./auditLog');
const { ensureWritableDirectory, writeJsonAtomic } = require('./configStoreUtils');
const { createDatabase } = require('./database');
const { RegistrationAuthError, verifySignedRegistrationRequest } = require('./registrationAuth');
const { SecurityStore } = require('./securityStore');
const {
    ValidationError,
    assertKeyId,
    assertMachineId,
    assertOptionalReason,
    assertRsaPublicKeyPem,
    assertString,
    assertVhdKeyword,
} = require('./validators');

const DEFAULT_PORT = Number(process.env.PORT || 8080);
const DEFAULT_VHD_KEYWORD = 'SDEZ';
const OTP_STEP_UP_WINDOW_MS = 60 * 1000;
const APP_VERSION = '2.0.0';

function createServiceSettingsStore(configDir, logger = console) {
    const settingsFile = path.join(configDir, 'vhd-config.json');

    function ensureDir() {
        ensureWritableDirectory(configDir);
    }

    function load() {
        ensureDir();
        if (!fs.existsSync(settingsFile)) {
            return {
                defaultVhdKeyword: DEFAULT_VHD_KEYWORD,
                updatedAt: null,
            };
        }

        try {
            const raw = JSON.parse(fs.readFileSync(settingsFile, 'utf8'));
            const keyword = assertVhdKeyword(raw.defaultVhdKeyword || raw.vhdKeyword || DEFAULT_VHD_KEYWORD);
            return {
                defaultVhdKeyword: keyword,
                updatedAt: raw.updatedAt || null,
            };
        } catch (error) {
            logger.error('读取服务配置失败，使用默认VHD关键词:', error.message);
            return {
                defaultVhdKeyword: DEFAULT_VHD_KEYWORD,
                updatedAt: null,
            };
        }
    }

    function save(nextConfig) {
        ensureDir();
        writeJsonAtomic(settingsFile, nextConfig);
        return nextConfig;
    }

    return {
        getPath() {
            return settingsFile;
        },
        load,
        getDefaultVhdKeyword() {
            return load().defaultVhdKeyword;
        },
        setDefaultVhdKeyword(keyword) {
            const normalizedKeyword = assertVhdKeyword(keyword);
            return save({
                defaultVhdKeyword: normalizedKeyword,
                vhdKeyword: normalizedKeyword,
                updatedAt: new Date().toISOString(),
            });
        },
    };
}

function asyncHandler(handler) {
    return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
}

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function parsePositiveInteger(value, fallbackValue, maxValue = 500) {
    const parsed = Number.parseInt(String(value || ''), 10);
    if (!Number.isFinite(parsed) || parsed <= 0) {
        return fallbackValue;
    }
    return Math.min(parsed, maxValue);
}

function normalizeFingerprint(value) {
    return String(value || '').replace(/:/g, '').trim().toUpperCase();
}

function buildAuditMetadata(req) {
    return {
        ip: req.ip,
        method: req.method,
        path: req.originalUrl,
        userAgent: req.get('user-agent') || '',
    };
}

function encryptWithPublicKeyRSA(publicKeyPem, plaintext) {
    const buffer = Buffer.from(plaintext, 'utf8');
    const encrypted = crypto.publicEncrypt({
        key: publicKeyPem,
        padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha1',
    }, buffer);
    return encrypted.toString('base64');
}

function regenerateSession(req) {
    return new Promise((resolve, reject) => {
        req.session.regenerate((error) => {
            if (error) {
                reject(error);
                return;
            }
            resolve();
        });
    });
}

function saveSession(req) {
    return new Promise((resolve, reject) => {
        req.session.save((error) => {
            if (error) {
                reject(error);
                return;
            }
            resolve();
        });
    });
}

function destroySession(req) {
    return new Promise((resolve, reject) => {
        req.session.destroy((error) => {
            if (error) {
                reject(error);
                return;
            }
            resolve();
        });
    });
}

async function createApp(options = {}) {
    const logger = options.logger || console;
    const configDir = options.configDir || process.env.CONFIG_PATH || __dirname;
    const securityStore = options.securityStore || new SecurityStore(configDir);
    const serviceSettingsStore = options.serviceSettingsStore || createServiceSettingsStore(configDir, logger);
    const auditLog = options.auditLog || new AuditLog(securityStore.getPaths().auditFile);
    const databaseFactory = options.databaseFactory || ((dbConfig) => createDatabase(dbConfig, logger));
    const sessionStore = options.sessionStore || new session.MemoryStore();
    const otpStepUpWindowMs = Number(options.otpStepUpWindowMs ?? OTP_STEP_UP_WINDOW_MS);
    const sessionSecrets = [crypto.randomBytes(48).toString('hex')];
    const providedDatabase = options.database || null;
    let providedDatabaseInitialized = false;

    const runtime = {
        auditLog,
        configDir,
        database: null,
        databaseError: null,
        initialized: false,
        logger,
        otpStepUpWindowMs,
        registrationNonceCache: new Map(),
        securityConfig: null,
        securityStore,
        serviceSettingsStore,
        sessionSecrets,
        writeAudit(req, entry) {
            try {
                auditLog.append({
                    ...buildAuditMetadata(req),
                    ...entry,
                });
            } catch (error) {
                logger.error('写入审计日志失败:', error.message);
            }
        },
    };

    async function connectDatabase(dbConfig) {
        const database = providedDatabase || databaseFactory(dbConfig);
        if (!database) {
            throw new Error('数据库实例创建失败');
        }

        if (!providedDatabase || !providedDatabaseInitialized) {
            if (typeof database.initialize === 'function') {
                await database.initialize();
            }
            if (providedDatabase) {
                providedDatabaseInitialized = true;
            }
        }

        runtime.database = database;
        runtime.databaseError = null;
        return database;
    }

    if (securityStore.isInitialized()) {
        runtime.initialized = true;
        runtime.securityConfig = securityStore.loadSecurityConfig();
        sessionSecrets.unshift(runtime.securityConfig.sessionSecret);

        try {
            await connectDatabase(runtime.securityConfig.dbConfig);
        } catch (error) {
            runtime.database = null;
            runtime.databaseError = error;
            logger.error('启动时连接数据库失败:', error.message);
        }
    }

    const app = express();
    app.set('trust proxy', options.trustProxy ?? 1);
    app.locals.runtime = runtime;

    app.use(helmet({
        contentSecurityPolicy: false,
    }));

    app.use((req, res, next) => {
        const origin = req.headers.origin;
        if (!origin) {
            next();
            return;
        }

        const allowedOrigins = runtime.initialized ? (runtime.securityConfig?.allowedOrigins || []) : [];
        const originAllowed = allowedOrigins.includes('*') || allowedOrigins.includes(origin);

        if (!originAllowed) {
            res.status(403).json({ success: false, error: 'Origin 不在允许列表中' });
            return;
        }

        res.setHeader('Access-Control-Allow-Origin', origin);
        res.setHeader('Access-Control-Allow-Credentials', 'true');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
        res.setHeader('Access-Control-Allow-Methods', 'GET,POST,DELETE,OPTIONS');
        res.setHeader('Vary', 'Origin');

        if (req.method === 'OPTIONS') {
            res.status(204).end();
            return;
        }

        next();
    });

    app.use(express.json({ limit: '256kb' }));
    app.use(express.urlencoded({ extended: false, limit: '64kb' }));
    app.use(session({
        name: 'vhdmount.sid',
        secret: sessionSecrets,
        resave: false,
        saveUninitialized: false,
        store: sessionStore,
        cookie: {
            httpOnly: true,
            sameSite: 'strict',
            secure: 'auto',
            maxAge: 24 * 60 * 60 * 1000,
        },
    }));

    const apiLimiter = rateLimit({
        windowMs: 60 * 1000,
        max: Number(process.env.API_RATE_LIMIT_MAX || 240),
        standardHeaders: true,
        legacyHeaders: false,
    });
    const loginLimiter = rateLimit({
        windowMs: 10 * 60 * 1000,
        max: Number(process.env.LOGIN_RATE_LIMIT_MAX || 20),
        standardHeaders: true,
        legacyHeaders: false,
    });
    const sensitiveLimiter = rateLimit({
        windowMs: 10 * 60 * 1000,
        max: Number(process.env.SENSITIVE_RATE_LIMIT_MAX || 40),
        standardHeaders: true,
        legacyHeaders: false,
    });

    app.use('/api', apiLimiter);

    function requireInitialized(req, res, next) {
        if (runtime.initialized) {
            next();
            return;
        }

        res.status(503).json({
            success: false,
            error: '服务尚未初始化',
            initializeRequired: true,
            pendingInitialization: !!securityStore.getPendingInitialization(),
        });
    }

    function requireDatabase(req, res, next) {
        if (runtime.database) {
            next();
            return;
        }

        res.status(503).json({
            success: false,
            error: '数据库当前不可用',
            details: runtime.databaseError ? runtime.databaseError.message : '数据库尚未连接',
        });
    }

    function requireAuth(req, res, next) {
        if (!runtime.initialized) {
            res.status(503).json({
                success: false,
                error: '服务尚未初始化',
                initializeRequired: true,
            });
            return;
        }

        if (req.session && req.session.isAuthenticated) {
            next();
            return;
        }

        res.status(401).json({
            success: false,
            message: '需要登录',
            requireAuth: true,
        });
    }

    function requireOtpStepUp(req, res, next) {
        const otpVerifiedUntil = Number(req.session?.otpVerifiedUntil || 0);
        if (otpVerifiedUntil >= Date.now()) {
            next();
            return;
        }

        res.status(403).json({
            success: false,
            error: '需要完成 OTP 二次验证',
            requireOtp: true,
        });
    }

    app.get('/api/init/status', (req, res) => {
        const pendingInitialization = securityStore.getPendingInitialization();
        res.json({
            success: true,
            initialized: runtime.initialized,
            pendingInitialization: !!pendingInitialization,
            pendingInitializationCreatedAt: pendingInitialization?.createdAt || null,
            pendingOtpIssuer: pendingInitialization?.issuer || null,
            pendingOtpAccountName: pendingInitialization?.accountName || null,
            databaseReady: !!runtime.database,
            databaseError: runtime.databaseError ? runtime.databaseError.message : null,
            defaultVhdKeyword: serviceSettingsStore.getDefaultVhdKeyword(),
            trustedRegistrationCertificateCount: runtime.initialized
                ? (runtime.securityConfig?.trustedRegistrationCertificates || []).length
                : 0,
        });
    });

    app.post('/api/init/prepare', sensitiveLimiter, asyncHandler(async (req, res) => {
        if (runtime.initialized) {
            throw createJsonError(409, '服务已经初始化完成');
        }

        const setup = securityStore.beginInitialization({
            issuer: req.body?.issuer,
            accountName: req.body?.accountName,
        });

        runtime.writeAudit(req, {
            type: 'init.prepare',
            actor: 'bootstrap',
            result: 'success',
        });

        res.status(201).json({
            success: true,
            ...setup,
        });
    }));

    app.post('/api/init/complete', sensitiveLimiter, asyncHandler(async (req, res) => {
        if (runtime.initialized) {
            throw createJsonError(409, '服务已经初始化完成');
        }

        const config = securityStore.buildSecurityConfig({
            adminPassword: req.body?.adminPassword,
            sessionSecret: req.body?.sessionSecret,
            totpCode: req.body?.totpCode,
            dbConfig: req.body?.dbConfig,
            allowedOrigins: req.body?.allowedOrigins,
            trustedRegistrationCertificates: req.body?.trustedRegistrationCertificates,
        });

        let database = null;
        try {
            database = await connectDatabase(config.dbConfig);
            securityStore.commitInitialization(config);
            runtime.initialized = true;
            runtime.securityConfig = securityStore.loadSecurityConfig();
            if (!runtime.sessionSecrets.includes(config.sessionSecret)) {
                runtime.sessionSecrets.unshift(config.sessionSecret);
            }

            const defaultVhdKeyword = req.body?.defaultVhdKeyword
                ? assertVhdKeyword(req.body.defaultVhdKeyword)
                : serviceSettingsStore.getDefaultVhdKeyword();
            serviceSettingsStore.setDefaultVhdKeyword(defaultVhdKeyword);

            runtime.writeAudit(req, {
                type: 'init.complete',
                actor: 'bootstrap',
                result: 'success',
                trustedRegistrationCertificateCount: (runtime.securityConfig.trustedRegistrationCertificates || []).length,
            });

            res.status(201).json({
                success: true,
                initialized: true,
                defaultVhdKeyword,
                trustedRegistrationCertificateCount: (runtime.securityConfig.trustedRegistrationCertificates || []).length,
            });
        } catch (error) {
            if (database && !providedDatabase && typeof database.close === 'function') {
                try {
                    await database.close();
                } catch (closeError) {
                    logger.error('初始化失败后关闭数据库连接失败:', closeError.message);
                }
            }

            runtime.database = null;
            runtime.databaseError = error;
            throw error;
        }
    }));

    app.post('/api/auth/login', loginLimiter, asyncHandler(async (req, res) => {
        if (!runtime.initialized) {
            res.status(503).json({
                success: false,
                error: '服务尚未初始化',
                initializeRequired: true,
            });
            return;
        }

        const password = String(req.body?.password || '');
        if (!password) {
            throw createJsonError(400, '请输入密码');
        }

        const passwordValid = securityStore.verifyPassword(password);
        if (!passwordValid) {
            runtime.writeAudit(req, {
                type: 'auth.login',
                actor: 'admin',
                result: 'failure',
            });
            res.status(401).json({
                success: false,
                message: '密码错误',
            });
            return;
        }

        await regenerateSession(req);
        req.session.isAuthenticated = true;
        req.session.authenticatedAt = Date.now();
        req.session.otpVerifiedUntil = 0;
        await saveSession(req);

        runtime.writeAudit(req, {
            type: 'auth.login',
            actor: 'admin',
            result: 'success',
        });

        res.json({
            success: true,
            message: '登录成功',
        });
    }));

    app.post('/api/auth/logout', asyncHandler(async (req, res) => {
        if (req.session) {
            await destroySession(req);
        }

        res.json({
            success: true,
            message: '已登出',
        });
    }));

    app.get('/api/auth/check', (req, res) => {
        res.json({
            initialized: runtime.initialized,
            isAuthenticated: !!req.session?.isAuthenticated,
            otpVerified: Number(req.session?.otpVerifiedUntil || 0) >= Date.now(),
            pendingInitialization: !runtime.initialized && !!securityStore.getPendingInitialization(),
        });
    });

    app.post('/api/auth/change-password', requireAuth, asyncHandler(async (req, res) => {
        const currentPassword = String(req.body?.currentPassword || '');
        const newPassword = String(req.body?.newPassword || '');
        const confirmPassword = String(req.body?.confirmPassword || '');

        if (!currentPassword || !newPassword || !confirmPassword) {
            throw createJsonError(400, '请填写所有密码字段');
        }
        if (newPassword !== confirmPassword) {
            throw createJsonError(400, '新密码和确认密码不匹配');
        }
        if (newPassword.length < 12) {
            throw createJsonError(400, '新密码长度至少为 12 位');
        }
        if (!securityStore.verifyPassword(currentPassword)) {
            runtime.writeAudit(req, {
                type: 'auth.change-password',
                actor: 'admin',
                result: 'failure',
            });
            throw createJsonError(401, '当前密码错误');
        }

        securityStore.updatePassword(newPassword);
        runtime.securityConfig = securityStore.loadSecurityConfig();
        req.session.otpVerifiedUntil = 0;
        await saveSession(req);

        runtime.writeAudit(req, {
            type: 'auth.change-password',
            actor: 'admin',
            result: 'success',
        });

        res.json({
            success: true,
            message: '密码修改成功',
        });
    }));

    app.post('/api/auth/otp/verify', requireAuth, sensitiveLimiter, asyncHandler(async (req, res) => {
        const code = assertString(req.body?.code, 'code', 6, 12);
        if (!securityStore.verifyTotp(code)) {
            runtime.writeAudit(req, {
                type: 'auth.otp.verify',
                actor: 'admin',
                result: 'failure',
            });
            throw createJsonError(401, 'OTP 校验失败');
        }

        req.session.otpVerifiedUntil = Date.now() + otpStepUpWindowMs;
        await saveSession(req);

        runtime.writeAudit(req, {
            type: 'auth.otp.verify',
            actor: 'admin',
            result: 'success',
        });

        res.json({
            success: true,
            otpVerifiedUntil: req.session.otpVerifiedUntil,
        });
    }));

    app.get('/api/auth/otp/status', requireAuth, (req, res) => {
        const otpVerifiedUntil = Number(req.session?.otpVerifiedUntil || 0);
        res.json({
            success: true,
            otpVerified: otpVerifiedUntil >= Date.now(),
            otpVerifiedUntil,
        });
    });

    app.get('/api/settings/default-vhd', requireAuth, (req, res) => {
        res.json({
            success: true,
            defaultVhdKeyword: serviceSettingsStore.getDefaultVhdKeyword(),
        });
    });

    const updateDefaultVhdHandler = asyncHandler(async (req, res) => {
        const nextKeyword = assertVhdKeyword(req.body?.vhdKeyword || req.body?.BootImageSelected);
        const config = serviceSettingsStore.setDefaultVhdKeyword(nextKeyword);

        runtime.writeAudit(req, {
            type: 'settings.default-vhd.update',
            actor: 'admin',
            result: 'success',
            defaultVhdKeyword: config.defaultVhdKeyword,
        });

        res.json({
            success: true,
            BootImageSelected: config.defaultVhdKeyword,
            defaultVhdKeyword: config.defaultVhdKeyword,
            message: '默认 VHD 关键词更新成功',
        });
    });

    app.post('/api/settings/default-vhd', requireAuth, updateDefaultVhdHandler);
    app.post('/api/set-vhd', requireAuth, updateDefaultVhdHandler);

    app.get('/api/boot-image-select', requireInitialized, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.query.machineId);
        const defaultVhdKeyword = serviceSettingsStore.getDefaultVhdKeyword();
        let machine = await runtime.database.getMachine(machineId);

        if (!machine) {
            machine = await runtime.database.upsertMachine(machineId, false, defaultVhdKeyword);
        }

        await runtime.database.updateMachineLastSeen(machineId);

        res.json({
            success: true,
            BootImageSelected: machine ? machine.vhd_keyword : defaultVhdKeyword,
            machineId,
            timestamp: new Date().toISOString(),
        });
    }));

    app.get('/api/protect', requireInitialized, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.query.machineId);
        const machine = await runtime.database.getMachine(machineId);
        if (!machine) {
            throw createJsonError(404, '机台不存在');
        }

        res.json({
            success: true,
            protected: machine.protected,
            machineId,
            timestamp: new Date().toISOString(),
        });
    }));

    app.post('/api/protect', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.body?.machineId);
        const protectedState = req.body?.protected;
        if (typeof protectedState !== 'boolean') {
            throw createJsonError(400, 'protected 状态必须是布尔值');
        }

        const machine = await runtime.database.updateMachineProtection(machineId, protectedState);
        if (!machine) {
            throw createJsonError(404, '机台不存在');
        }

        runtime.writeAudit(req, {
            type: 'machine.protection.update',
            actor: 'admin',
            result: 'success',
            machineId,
            protected: protectedState,
        });

        res.json({
            success: true,
            protected: machine.protected,
            machineId,
            message: '机台保护状态已更新',
        });
    }));

    app.get('/api/machines', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machines = await runtime.database.getAllMachines();
        res.json({
            success: true,
            machines,
            count: machines.length,
            timestamp: new Date().toISOString(),
        });
    }));

    app.post('/api/machines', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.body?.machineId);
        const protectedState = typeof req.body?.protected === 'boolean' ? req.body.protected : false;
        const vhdKeyword = req.body?.vhdKeyword
            ? assertVhdKeyword(req.body.vhdKeyword)
            : serviceSettingsStore.getDefaultVhdKeyword();
        const evhdPassword = req.body?.evhdPassword;

        if (evhdPassword != null && (typeof evhdPassword !== 'string' || evhdPassword.length < 1 || evhdPassword.length > 512)) {
            throw createJsonError(400, 'EVHD 密码必须是 1-512 个字符');
        }

        const existingMachine = await runtime.database.getMachine(machineId);
        if (existingMachine) {
            throw createJsonError(409, '机台已存在');
        }

        let machine = await runtime.database.upsertMachine(machineId, protectedState, vhdKeyword);
        if (!machine) {
            throw createJsonError(500, '新增机台失败');
        }

        if (typeof evhdPassword === 'string' && evhdPassword.length > 0) {
            machine = await runtime.database.updateMachineEvhdPassword(machineId, evhdPassword);
            if (!machine) {
                throw createJsonError(500, '新增机台后写入 EVHD 密码失败');
            }
        }

        runtime.writeAudit(req, {
            type: 'machine.create',
            actor: 'admin',
            result: 'success',
            machineId,
            protected: protectedState,
            vhdKeyword,
        });

        res.status(201).json({
            success: true,
            machine,
            message: '机台已添加',
        });
    }));

    app.get('/api/machines/:machineId', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const machine = await runtime.database.getMachine(machineId);
        if (!machine) {
            throw createJsonError(404, '机台不存在');
        }

        res.json({
            success: true,
            machine,
        });
    }));

    app.post('/api/machines/:machineId/keys', sensitiveLimiter, requireInitialized, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const keyId = assertKeyId(req.body?.keyId);
        const keyType = String(req.body?.keyType || 'RSA').trim().toUpperCase();
        const pubkeyPem = assertRsaPublicKeyPem(req.body?.pubkeyPem);

        if (keyType !== 'RSA') {
            throw createJsonError(400, '当前仅支持 RSA 密钥');
        }

        const verification = verifySignedRegistrationRequest({
            machineId,
            keyId,
            keyType,
            pubkeyPem,
            registrationCertificatePem: assertString(req.body?.registrationCertificatePem, 'registrationCertificatePem', 64, 32768),
            signature: assertString(req.body?.signature, 'signature', 32, 32768),
            timestamp: req.body?.timestamp,
            nonce: assertString(req.body?.nonce, 'nonce', 16, 256),
            trustedCertificates: runtime.securityStore.listTrustedRegistrationCertificates(),
            nonceCache: runtime.registrationNonceCache,
        });

        const machine = await runtime.database.updateMachineKey(machineId, {
            keyId,
            keyType,
            pubkeyPem,
            registrationCertFingerprint: verification.fingerprint256,
            registrationCertSubject: verification.subject,
        });

        if (!machine) {
            throw createJsonError(500, '注册机台公钥失败');
        }

        await runtime.database.updateMachineLastSeen(machineId);

        runtime.writeAudit(req, {
            type: 'machine.registration.submit',
            actor: 'machine',
            result: 'success',
            machineId,
            keyId,
            registrationCertFingerprint: verification.fingerprint256,
        });

        res.status(202).json({
            success: true,
            machineId,
            keyId: machine.key_id,
            keyType: machine.key_type,
            approved: machine.approved,
            revoked: machine.revoked,
            registrationCertFingerprint: machine.registration_cert_fingerprint,
            message: '机台公钥已注册，待管理员审批',
        });
    }));

    app.post('/api/machines/:machineId/approve', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const approved = typeof req.body?.approved === 'boolean' ? req.body.approved : true;
        const machine = await runtime.database.approveMachine(machineId, approved);

        if (!machine) {
            throw createJsonError(404, '机台不存在或审批失败');
        }

        runtime.writeAudit(req, {
            type: 'machine.approval.update',
            actor: 'admin',
            result: 'success',
            machineId,
            approved,
        });

        res.json({
            success: true,
            machineId,
            approved: machine.approved,
            approvedAt: machine.approved_at,
            message: approved ? '已审批通过' : '已取消审批',
        });
    }));

    app.post('/api/machines/:machineId/revoke', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const machine = await runtime.database.revokeMachineKey(machineId);
        if (!machine) {
            throw createJsonError(404, '机台不存在或重置失败');
        }

        runtime.writeAudit(req, {
            type: 'machine.registration.reset',
            actor: 'admin',
            result: 'success',
            machineId,
        });

        res.json({
            success: true,
            machineId,
            approved: machine.approved,
            keyId: machine.key_id,
            message: '已重置机台注册状态',
        });
    }));

    app.post('/api/machines/:machineId/vhd', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const vhdKeyword = assertVhdKeyword(req.body?.vhdKeyword);

        let machine = await runtime.database.updateMachineVhdKeyword(machineId, vhdKeyword);
        if (!machine) {
            machine = await runtime.database.upsertMachine(machineId, false, vhdKeyword);
        }

        runtime.writeAudit(req, {
            type: 'machine.vhd.update',
            actor: 'admin',
            result: 'success',
            machineId,
            vhdKeyword,
        });

        res.json({
            success: true,
            machineId,
            vhdKeyword: machine.vhd_keyword,
            message: '机台 VHD 关键词已更新',
        });
    }));

    app.post('/api/machines/:machineId/evhd-password', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const evhdPassword = req.body?.evhdPassword;
        if (typeof evhdPassword !== 'string' || evhdPassword.length < 1 || evhdPassword.length > 512) {
            throw createJsonError(400, 'EVHD 密码必须是 1-512 个字符');
        }

        let machine = await runtime.database.updateMachineEvhdPassword(machineId, evhdPassword);
        if (!machine) {
            await runtime.database.upsertMachine(machineId, false, serviceSettingsStore.getDefaultVhdKeyword());
            machine = await runtime.database.updateMachineEvhdPassword(machineId, evhdPassword);
        }

        runtime.writeAudit(req, {
            type: 'machine.evhd-password.update',
            actor: 'admin',
            result: 'success',
            machineId,
        });

        res.json({
            success: true,
            machineId,
            message: 'EVHD 密码已更新',
        });
    }));

    app.get('/api/evhd-envelope', requireInitialized, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.query.machineId);
        const machine = await runtime.database.getMachine(machineId);

        if (!machine) {
            throw createJsonError(404, '机台不存在');
        }

        await runtime.database.updateMachineLastSeen(machineId);

        if (machine.revoked) {
            throw createJsonError(403, '机台密钥已吊销');
        }
        if (!machine.approved) {
            throw createJsonError(403, '机台密钥未审批');
        }
        if (!machine.pubkey_pem) {
            throw createJsonError(400, '机台未注册公钥');
        }

        const evhdPassword = await runtime.database.getMachineEvhdPassword(machineId);
        if (!evhdPassword) {
            throw createJsonError(404, '机台未配置 EVHD 密码');
        }

        const ciphertext = encryptWithPublicKeyRSA(machine.pubkey_pem, evhdPassword);
        res.json({
            success: true,
            machineId,
            approved: machine.approved,
            revoked: machine.revoked,
            keyId: machine.key_id,
            keyType: machine.key_type,
            ciphertext,
        });
    }));

    app.get('/api/evhd-password/plain', requireAuth, requireOtpStepUp, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.query.machineId);
        const reason = assertOptionalReason(req.query.reason);
        const evhdPassword = await runtime.database.getMachineEvhdPassword(machineId);
        if (!evhdPassword) {
            throw createJsonError(404, '机台不存在或未设置 EVHD 密码');
        }

        runtime.writeAudit(req, {
            type: 'machine.evhd-password.read',
            actor: 'admin',
            result: 'success',
            machineId,
            reason,
        });

        res.json({
            success: true,
            machineId,
            evhdPassword,
        });
    }));

    app.delete('/api/machines/:machineId', requireAuth, requireDatabase, asyncHandler(async (req, res) => {
        const machineId = assertMachineId(req.params.machineId);
        const deletedMachine = await runtime.database.deleteMachine(machineId);
        if (!deletedMachine) {
            throw createJsonError(404, '机台不存在');
        }

        runtime.writeAudit(req, {
            type: 'machine.delete',
            actor: 'admin',
            result: 'success',
            machineId,
        });

        res.json({
            success: true,
            machineId,
            message: '机台已删除',
        });
    }));

    app.get('/api/security/trusted-certificates', requireAuth, requireOtpStepUp, (req, res) => {
        res.json({
            success: true,
            certificates: securityStore.listTrustedRegistrationCertificates(),
        });
    });

    app.post('/api/security/trusted-certificates', requireAuth, requireOtpStepUp, asyncHandler(async (req, res) => {
        const certificatePem = assertString(req.body?.certificatePem, 'certificatePem', 64, 32768);
        const name = req.body?.name ? assertString(req.body.name, 'name', 1, 128) : undefined;
        const certificate = securityStore.addTrustedRegistrationCertificate({
            name,
            certificatePem,
        });

        runtime.securityConfig = securityStore.loadSecurityConfig();
        runtime.writeAudit(req, {
            type: 'security.trusted-certificate.add',
            actor: 'admin',
            result: 'success',
            fingerprint256: certificate.fingerprint256,
        });

        res.status(201).json({
            success: true,
            certificate,
        });
    }));

    app.delete('/api/security/trusted-certificates/:fingerprint', requireAuth, requireOtpStepUp, asyncHandler(async (req, res) => {
        const fingerprint = normalizeFingerprint(req.params.fingerprint);
        if (!fingerprint) {
            throw createJsonError(400, '证书指纹不能为空');
        }

        const removed = securityStore.removeTrustedRegistrationCertificate(fingerprint);
        if (!removed) {
            throw createJsonError(404, '未找到对应的可信注册证书');
        }

        runtime.securityConfig = securityStore.loadSecurityConfig();
        runtime.writeAudit(req, {
            type: 'security.trusted-certificate.remove',
            actor: 'admin',
            result: 'success',
            fingerprint256: fingerprint,
        });

        res.json({
            success: true,
            fingerprint256: fingerprint,
        });
    }));

    app.get('/api/audit', requireAuth, asyncHandler(async (req, res) => {
        const type = req.query.type ? assertString(req.query.type, 'type', 1, 64) : undefined;
        const limit = parsePositiveInteger(req.query.limit, 100, 500);
        const entries = auditLog.read({ type, limit });
        res.json({
            success: true,
            entries,
            count: entries.length,
        });
    }));

    function buildStatusPayload(status) {
        return {
            success: true,
            status,
            version: APP_VERSION,
            initialized: runtime.initialized,
            databaseReady: !!runtime.database,
            databaseError: runtime.databaseError ? runtime.databaseError.message : null,
            defaultVhdKeyword: serviceSettingsStore.getDefaultVhdKeyword(),
            trustedRegistrationCertificateCount: runtime.initialized
                ? (runtime.securityConfig?.trustedRegistrationCertificates || []).length
                : 0,
            uptime: process.uptime(),
            timestamp: new Date().toISOString(),
        };
    }

    app.get('/api/health', (req, res) => {
        res.json(buildStatusPayload('ok'));
    });

    app.get('/api/status', (req, res) => {
        res.json(buildStatusPayload('running'));
    });

    app.get('/', (req, res) => {
        res.status(410).json({
            success: false,
            error: '旧 Web 管理前端已废弃，请使用新的 Flutter 管理客户端。',
        });
    });

    app.use((req, res) => {
        res.status(404).json({
            success: false,
            error: '页面未找到',
        });
    });

    app.use((error, req, res, next) => {
        if (res.headersSent) {
            next(error);
            return;
        }

        const statusCode = Number(error.statusCode || 500);

        if (statusCode >= 500) {
            logger.error('服务器错误:', error);
        }

        if (error instanceof ValidationError || error instanceof RegistrationAuthError) {
            res.status(error.statusCode || statusCode).json({
                success: false,
                error: error.message,
            });
            return;
        }

        res.status(statusCode).json({
            success: false,
            error: error.message || '服务器内部错误',
            ...(error.initializeRequired ? { initializeRequired: true } : {}),
            ...(error.requireAuth ? { requireAuth: true } : {}),
        });
    });

    return {
        app,
        runtime,
    };
}

async function startServer(options = {}) {
    const logger = options.logger || console;
    const { app, runtime } = await createApp(options);
    const port = Number(options.port || DEFAULT_PORT);

    const server = await new Promise((resolve, reject) => {
        const instance = app.listen(port, () => resolve(instance));
        instance.on('error', reject);
    });

    logger.log('='.repeat(60));
    logger.log('VHD Select Server 已启动');
    logger.log(`服务器地址: http://localhost:${port}`);
    logger.log(`初始化状态: ${runtime.initialized ? '已完成' : '未初始化'}`);
    logger.log(`默认 VHD 关键词: ${runtime.serviceSettingsStore.getDefaultVhdKeyword()}`);
    logger.log('='.repeat(60));

    async function closeServer(signal) {
        logger.log(`${signal} received, shutting down...`);

        await new Promise((resolve) => {
            server.close(() => resolve());
        });

        if (runtime.database && !options.database && typeof runtime.database.close === 'function') {
            await runtime.database.close();
        }
    }

    if (!options.disableSignalHandlers) {
        process.once('SIGINT', () => {
            closeServer('SIGINT')
                .then(() => process.exit(0))
                .catch((error) => {
                    logger.error('关闭服务器失败:', error);
                    process.exit(1);
                });
        });

        process.once('SIGTERM', () => {
            closeServer('SIGTERM')
                .then(() => process.exit(0))
                .catch((error) => {
                    logger.error('关闭服务器失败:', error);
                    process.exit(1);
                });
        });
    }

    return {
        app,
        runtime,
        server,
        closeServer,
    };
}

if (require.main === module) {
    startServer().catch((error) => {
        console.error('服务器启动失败:', error);
        process.exit(1);
    });
}

module.exports = {
    APP_VERSION,
    createApp,
    createServiceSettingsStore,
    encryptWithPublicKeyRSA,
    startServer,
};