const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const { DeploymentStore } = require('./deploymentStore');
const { ValidationError } = require('./validators');

const PACKAGE_MAX_BYTES = 2 * 1024 * 1024 * 1024; // 2GB
const MACHINE_REQUEST_SIGNATURE_WINDOW_MS = 5 * 60 * 1000;
const TASK_LEASE_SECONDS = Number(process.env.DEPLOYMENT_TASK_LEASE_SECONDS || 30 * 60);
const UA_PREFIX = 'VHDMount/';

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function assertMachineId(value) {
    const id = String(value || '').trim();
    if (!id || id.length > 64) {
        throw new ValidationError('machineId 不能为空且长度不超过 64');
    }
    return id;
}

function assertPackageType(value) {
    const type = String(value || '').trim();
    if (!['software-deploy', 'file-deploy'].includes(type)) {
        throw new ValidationError('type 必须是 software-deploy 或 file-deploy');
    }
    return type;
}

function assertString(value, name, min = 1, max = 256) {
    const str = String(value || '').trim();
    if (str.length < min || str.length > max) {
        throw new ValidationError(`${name} 长度必须在 ${min}-${max} 之间`);
    }
    return str;
}

function assertNonEmptyStringArray(value, name) {
    const arr = Array.isArray(value) ? value : [];
    if (arr.length === 0) {
        throw new ValidationError(`${name} 不能为空数组`);
    }
    return arr.map((v) => String(v || '').trim()).filter(Boolean);
}

function assertDeploymentRecordStatus(value) {
    const status = String(value || '').trim();
    if (!['success', 'failed', 'uninstalled'].includes(status)) {
        throw new ValidationError('status 必须是 success、failed 或 uninstalled');
    }
    return status;
}

function assertOptionalIsoTimestamp(value, name) {
    if (value == null || String(value).trim() === '') {
        return null;
    }
    const timestamp = String(value).trim();
    const parsed = Date.parse(timestamp);
    if (!Number.isFinite(parsed)) {
        throw new ValidationError(`${name} 必须是合法的 ISO 时间`);
    }
    return new Date(parsed).toISOString();
}

function computeFileHash(filePath) {
    const hash = crypto.createHash('sha256');
    const data = fs.readFileSync(filePath);
    hash.update(data);
    return hash.digest('hex');
}

function createCtrCipher(key, iv, offset = 0) {
    const blockSize = 16;
    const counter = Math.floor(offset / blockSize);
    const blockOffset = offset % blockSize;

    // IV = nonce(8 bytes) + counter(8 bytes big-endian)
    const ivBuf = Buffer.alloc(16);
    iv.copy(ivBuf, 0, 0, 8);
    const counterBuf = Buffer.alloc(8);
    counterBuf.writeBigUInt64BE(BigInt(counter), 0);
    counterBuf.copy(ivBuf, 8);

    const cipher = crypto.createCipheriv('aes-256-ctr', key, ivBuf);
    if (blockOffset > 0) {
        cipher.update(Buffer.alloc(blockOffset));
    }
    return cipher;
}

function buildDeploymentRoutes(options = {}) {
    const deploymentStore = options.deploymentStore || new DeploymentStore(options.configDir);
    const encryptWithPublicKeyRSA = options.encryptWithPublicKeyRSA;

    function isValidDeploymentUserAgent(value) {
        const ua = String(value || '').trim();
        return ua.startsWith(UA_PREFIX);
    }

    function buildMachineRequestSigningPayload({
        machineId,
        keyId,
        method,
        path,
        timestamp,
        nonce,
        bodyHash,
    }) {
        return [
            'VHDMountDeploymentRequestV1',
            String(machineId || '').trim(),
            String(keyId || '').trim(),
            String(method || '').trim().toUpperCase(),
            String(path || '').trim(),
            String(timestamp || '').trim(),
            String(nonce || '').trim(),
            String(bodyHash || '').trim().toLowerCase(),
        ].join('\n');
    }

    function computeRequestBodyHash(req) {
        if (typeof req.rawBody === 'string') {
            return crypto.createHash('sha256').update(req.rawBody, 'utf8').digest('hex');
        }

        if (req.body != null && Object.keys(req.body).length > 0) {
            return crypto.createHash('sha256').update(JSON.stringify(req.body), 'utf8').digest('hex');
        }

        return crypto.createHash('sha256').update('', 'utf8').digest('hex');
    }

    function cleanupMachineRequestNonces(nonceCache) {
        const cutoff = Date.now() - MACHINE_REQUEST_SIGNATURE_WINDOW_MS;
        for (const [nonceKey, seenAt] of nonceCache.entries()) {
            if (Number(seenAt) < cutoff) {
                nonceCache.delete(nonceKey);
            }
        }
    }

    async function requireVerifiedMachineRequest(req, options = {}) {
        const runtime = req.app.locals.runtime;
        const machineId = assertMachineId(req.params.machineId);
        const machine = await runtime.database.getMachine(machineId);
        if (!machine) {
            throw createJsonError(404, '机台不存在');
        }
        if (!machine.pubkey_pem) {
            throw createJsonError(400, '机台未注册公钥');
        }

        if (options.requireApproved !== false) {
            if (machine.revoked) {
                throw createJsonError(403, '机台密钥已吊销');
            }
            if (!machine.approved) {
                throw createJsonError(403, '机台密钥未审批');
            }
        }

        const keyIdHeader = req.get('x-vhdm-keyid');
        const timestampHeader = req.get('x-vhdm-timestamp');
        const nonceHeader = req.get('x-vhdm-nonce');
        const signatureHeader = req.get('x-vhdm-signature');
        if (!keyIdHeader || !timestampHeader || !nonceHeader || !signatureHeader) {
            throw createJsonError(401, '需要机台签名认证');
        }

        const keyId = assertString(keyIdHeader, 'x-vhdm-keyid', 1, 256);
        const timestampRaw = assertString(timestampHeader, 'x-vhdm-timestamp', 1, 32);
        const nonce = assertString(nonceHeader, 'x-vhdm-nonce', 16, 256);
        const signatureBase64 = assertString(signatureHeader, 'x-vhdm-signature', 32, 8192);

        if (machine.key_id && keyId !== machine.key_id) {
            throw createJsonError(403, '机台 keyId 不匹配');
        }

        const timestamp = Number.parseInt(timestampRaw, 10);
        if (!Number.isFinite(timestamp)) {
            throw createJsonError(400, '机台签名时间戳无效');
        }
        if (Math.abs(Date.now() - timestamp) > MACHINE_REQUEST_SIGNATURE_WINDOW_MS) {
            throw createJsonError(401, '机台签名已过期');
        }

        const bodyHash = computeRequestBodyHash(req);
        const payload = buildMachineRequestSigningPayload({
            machineId,
            keyId,
            method: req.method,
            path: req.path,
            timestamp,
            nonce,
            bodyHash,
        });

        let signatureBytes;
        try {
            signatureBytes = Buffer.from(signatureBase64, 'base64');
        } catch {
            throw createJsonError(400, '机台签名格式无效');
        }

        const verifier = crypto.createVerify('RSA-SHA256');
        verifier.update(payload, 'utf8');
        verifier.end();
        if (!verifier.verify(machine.pubkey_pem, signatureBytes)) {
            throw createJsonError(401, '机台签名校验失败');
        }

        cleanupMachineRequestNonces(runtime.deploymentRequestNonceCache);
        const nonceKey = `${machineId}:${nonce}`;
        if (runtime.deploymentRequestNonceCache.has(nonceKey)) {
            throw createJsonError(409, '机台签名 nonce 重复');
        }
        runtime.deploymentRequestNonceCache.set(nonceKey, Date.now());

        return {
            machineId,
            machine,
            keyId,
        };
    }

    function asyncHandler(handler) {
        return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
    }

    function requireAuth(req, res, next) {
        const runtime = req.app.locals.runtime;
        if (!runtime.initialized) {
            res.status(503).json({ success: false, error: '服务尚未初始化', initializeRequired: true });
            return;
        }
        if (req.session?.isAuthenticated) {
            next();
            return;
        }
        res.status(401).json({ success: false, message: '需要登录', requireAuth: true });
    }

    function requireDatabase(req, res, next) {
        const runtime = req.app.locals.runtime;
        if (runtime.database) {
            next();
            return;
        }
        res.status(503).json({ success: false, error: '数据库当前不可用' });
    }

    // ---------- 管理员：部署包管理 ----------

    async function uploadPackage(req, res) {
        const runtime = req.app.locals.runtime;
        if (!req.files || !req.files.package) {
            throw createJsonError(400, '请上传部署包文件');
        }
        if (!req.files.signature) {
            throw createJsonError(400, '请上传签名文件');
        }

        const pkgFile = req.files.package;
        const sigFile = req.files.signature;

        if (pkgFile.size > PACKAGE_MAX_BYTES) {
            throw createJsonError(413, `部署包大小超过上限 ${PACKAGE_MAX_BYTES / 1024 / 1024}MB`);
        }

        const name = assertString(req.body?.name, 'name');
        const version = assertString(req.body?.version, 'version', 1, 64);
        const type = assertPackageType(req.body?.type);
        const signer = assertString(req.body?.signer, 'signer');

        if (!pkgFile.tempFilePath || !sigFile.tempFilePath) {
            throw createJsonError(500, '上传临时文件不可用');
        }

        const pkg = await deploymentStore.createPackageWithFiles(runtime.database, {
            name, version, type, signer,
            fileSize: pkgFile.size,
        }, {
            packageSourcePath: pkgFile.tempFilePath,
            signatureSourcePath: sigFile.tempFilePath,
        });

        runtime.writeAudit(req, {
            type: 'deployment.package.upload',
            actor: 'admin',
            result: 'success',
            packageId: pkg.packageId,
        });

        res.status(201).json({
            success: true,
            package: pkg,
        });
    }

    async function listPackages(req, res) {
        const runtime = req.app.locals.runtime;
        const packages = await deploymentStore.listPackages(runtime.database);
        res.json({ success: true, packages, count: packages.length });
    }

    async function getPackage(req, res) {
        const runtime = req.app.locals.runtime;
        const packageId = assertString(req.params.id, 'packageId');
        const pkg = await deploymentStore.getPackage(runtime.database, packageId);
        if (!pkg) {
            throw createJsonError(404, '部署包不存在');
        }
        res.json({ success: true, package: pkg });
    }

    async function deletePackage(req, res) {
        const runtime = req.app.locals.runtime;
        const packageId = assertString(req.params.id, 'packageId');
        const pkg = await deploymentStore.deletePackage(runtime.database, packageId);
        if (!pkg) {
            throw createJsonError(404, '部署包不存在');
        }

        runtime.writeAudit(req, {
            type: 'deployment.package.delete',
            actor: 'admin',
            result: 'success',
            packageId,
        });

        res.json({ success: true, packageId, message: '部署包已删除' });
    }

    // ---------- 管理员：部署任务管理 ----------

    async function createTask(req, res) {
        const runtime = req.app.locals.runtime;
        const packageId = assertString(req.body?.packageId, 'packageId');
        const targetMachineIds = assertNonEmptyStringArray(req.body?.targetMachineIds, 'targetMachineIds');
        const scheduledAt = req.body?.scheduledAt || null;

        const pkg = await deploymentStore.getPackage(runtime.database, packageId);
        if (!pkg) {
            throw createJsonError(404, '部署包不存在');
        }

        const machineIds = targetMachineIds.map((machineId) => assertMachineId(machineId));
        const tasks = await deploymentStore.createTasks(runtime.database, {
            packageId,
            machineIds,
            scheduledAt,
        });

        runtime.writeAudit(req, {
            type: 'deployment.task.create',
            actor: 'admin',
            result: 'success',
            packageId,
            taskCount: tasks.length,
        });

        res.status(201).json({
            success: true,
            tasks,
            count: tasks.length,
        });
    }

    async function deleteTask(req, res) {
        const runtime = req.app.locals.runtime;
        const taskId = assertString(req.params.id, 'taskId');
        const task = await deploymentStore.deleteTask(runtime.database, taskId);
        if (!task) {
            throw createJsonError(404, '任务不存在');
        }

        runtime.writeAudit(req, {
            type: 'deployment.task.delete',
            actor: 'admin',
            result: 'success',
            taskId,
        });

        res.json({ success: true, taskId, message: '部署任务已删除' });
    }

    async function listTasks(req, res) {
        const runtime = req.app.locals.runtime;
        const tasks = await deploymentStore.listTasks(runtime.database, {
            machineId: req.query.machineId || undefined,
            status: req.query.status || undefined,
        });
        res.json({ success: true, tasks, count: tasks.length });
    }

    // ---------- 机台接口 ----------

    async function getPendingTasks(req, res) {
        const runtime = req.app.locals.runtime;
        const { machineId, machine } = await requireVerifiedMachineRequest(req);
        await runtime.database.updateMachineLastSeen(machineId);

        const pendingTasks = await deploymentStore.claimPendingTasks(runtime.database, machineId, {
            leaseDurationSeconds: TASK_LEASE_SECONDS,
        });
        const tasks = [];

        for (const task of pendingTasks) {
            // ZIP 与签名文件分别使用独立的 AES 参数，避免 CTR keystream 复用
            const packageAesKey = crypto.randomBytes(32);
            const packageIv = Buffer.concat([crypto.randomBytes(8), Buffer.alloc(8)]);
            const packageAesKeyBase64 = packageAesKey.toString('base64');
            const packageIvBase64 = packageIv.toString('base64');

            const signatureAesKey = crypto.randomBytes(32);
            const signatureIv = Buffer.concat([crypto.randomBytes(8), Buffer.alloc(8)]);
            const signatureAesKeyBase64 = signatureAesKey.toString('base64');
            const signatureIvBase64 = signatureIv.toString('base64');

            // 用机台 RSA 公钥加密 AES 密钥
            const keyCipher = encryptWithPublicKeyRSA(machine.pubkey_pem, packageAesKeyBase64);
            const signatureKeyCipher = encryptWithPublicKeyRSA(machine.pubkey_pem, signatureAesKeyBase64);

            const packageToken = await deploymentStore.createDownloadToken(runtime.database, {
                taskId: task.taskId,
                machineId,
                packageId: task.packageId,
                resourceType: 'package',
                aesKey: packageAesKeyBase64,
                aesIv: packageIvBase64,
            });
            const signatureToken = await deploymentStore.createDownloadToken(runtime.database, {
                taskId: task.taskId,
                machineId,
                packageId: task.packageId,
                resourceType: 'signature',
                aesKey: signatureAesKeyBase64,
                aesIv: signatureIvBase64,
            });

            tasks.push({
                taskId: task.taskId,
                packageId: task.packageId,
                taskType: task.taskType,
                packageName: task.packageName,
                packageVersion: task.packageVersion,
                packageType: task.packageType,
                packageSize: task.packageSize,
                downloadUrl: `/api/deployments/packages/${task.packageId}/download?token=${packageToken}&machineId=${encodeURIComponent(machineId)}&expires=${Date.now() + 3600000}`,
                signatureUrl: `/api/deployments/packages/${task.packageId}/signature?token=${signatureToken}&machineId=${encodeURIComponent(machineId)}&expires=${Date.now() + 3600000}`,
                keyCipher,
                iv: packageIvBase64,
                signatureKeyCipher,
                signatureIv: signatureIvBase64,
            });

        }

        res.json({ success: true, tasks });
    }

    async function reportTaskStatus(req, res) {
        const runtime = req.app.locals.runtime;
        const { machineId } = await requireVerifiedMachineRequest(req);
        const taskId = assertString(req.params.taskId, 'taskId');
        const status = assertString(req.body?.status, 'status');
        const errorMessage = req.body?.errorMessage || null;

        if (!['downloading', 'running', 'success', 'failed'].includes(status)) {
            throw createJsonError(400, 'status 必须是 downloading、running、success 或 failed');
        }

        const currentTask = await deploymentStore.getTask(runtime.database, taskId);
        if (!currentTask) {
            throw createJsonError(404, '任务不存在');
        }
        if (currentTask.machineId !== machineId) {
            throw createJsonError(403, '任务不属于该机台');
        }

        const task = await deploymentStore.updateTaskStatus(runtime.database, taskId, status, errorMessage, {
            leaseDurationSeconds: TASK_LEASE_SECONDS,
        });
        if (!task) {
            throw createJsonError(404, '任务不存在');
        }

        res.json({ success: true, task });
    }

    async function syncRecords(req, res) {
        const runtime = req.app.locals.runtime;
        const { machineId } = await requireVerifiedMachineRequest(req);
        const records = Array.isArray(req.body?.records) ? req.body.records : [];

        const synced = [];
        for (const record of records) {
            const syncedRecord = await deploymentStore.syncDeploymentRecord(runtime.database, {
                recordId: assertString(record.recordId, 'recordId'),
                machineId,
                packageId: assertString(record.packageId, 'packageId'),
                name: assertString(record.name, 'name'),
                version: assertString(record.version, 'version', 1, 64),
                type: assertPackageType(record.type),
                targetPath: record.targetPath || null,
                status: assertDeploymentRecordStatus(record.status || 'success'),
                deployedAt: assertOptionalIsoTimestamp(record.deployedAt, 'deployedAt') || new Date().toISOString(),
                uninstalledAt: assertOptionalIsoTimestamp(record.uninstalledAt, 'uninstalledAt'),
            });
            synced.push(syncedRecord);
        }

        res.json({ success: true, synced: synced.length });
    }

    // ---------- 管理员：历史与卸载 ----------

    async function getMachineHistory(req, res) {
        const runtime = req.app.locals.runtime;
        const machineId = assertMachineId(req.params.machineId);
        const records = await deploymentStore.listMachineRecords(runtime.database, machineId);
        res.json({ success: true, records, count: records.length });
    }

    async function triggerUninstall(req, res) {
        const runtime = req.app.locals.runtime;
        const machineId = assertMachineId(req.params.machineId);
        const recordId = assertString(req.params.recordId, 'recordId');

        const record = await deploymentStore.getRecordById(runtime.database, recordId);
        if (!record) {
            throw createJsonError(404, '部署记录不存在');
        }
        if (record.machineId !== machineId) {
            throw createJsonError(403, '部署记录不属于该机台');
        }
        if (record.status === 'uninstalled') {
            throw createJsonError(409, '该部署记录已卸载');
        }

        const task = await deploymentStore.createTask(runtime.database, {
            packageId: record.packageId,
            machineId,
            taskType: 'uninstall',
        });

        runtime.writeAudit(req, {
            type: 'deployment.task.uninstall',
            actor: 'admin',
            result: 'success',
            recordId,
            machineId,
            taskId: task.taskId,
        });

        res.json({ success: true, task, message: '卸载任务已创建' });
    }

    // ---------- 下载接口（令牌验证 + UA 验证 + Range 支持） ----------

    async function downloadPackage(req, res) {
        const runtime = req.app.locals.runtime;
        const packageId = assertString(req.params.id, 'packageId');
        const token = assertString(req.query.token, 'token');
        const machineId = assertString(req.query.machineId, 'machineId');

        if (!isValidDeploymentUserAgent(req.get('user-agent'))) {
            res.status(403).json({ success: false, error: 'User-Agent 校验失败' });
            return;
        }

        const tokenRecord = await deploymentStore.validateToken(runtime.database, token, {
            machineId,
            packageId,
            resourceType: 'package',
        });
        if (!tokenRecord) {
            res.status(403).json({ success: false, error: '下载令牌无效或已过期' });
            return;
        }

        const pkg = await deploymentStore.getPackage(runtime.database, packageId);
        if (!pkg) {
            res.status(404).json({ success: false, error: '部署包不存在' });
            return;
        }

        if (!fs.existsSync(pkg.filePath)) {
            res.status(404).json({ success: false, error: '部署包文件不存在' });
            return;
        }

        await deploymentStore.markTokenUsed(runtime.database, token);

        const stat = fs.statSync(pkg.filePath);
        const range = req.headers.range;

        // 从 token 获取 AES 加密参数，对文件流做 AES-256-CTR 动态加密
        const aesKey = Buffer.from(tokenRecord.aes_key || '', 'base64');
        const aesIv = Buffer.from(tokenRecord.aes_iv || '', 'base64');
        let start = 0;
        let end = stat.size - 1;
        if (range) {
            const parts = range.replace(/bytes=/, '').split('-');
            start = parseInt(parts[0], 10);
            end = parts[1] ? parseInt(parts[1], 10) : stat.size - 1;
        }
        const cipher = createCtrCipher(aesKey, aesIv, start);
        const chunksize = end - start + 1;

        if (range) {
            res.writeHead(206, {
                'Content-Range': `bytes ${start}-${end}/${stat.size}`,
                'Accept-Ranges': 'bytes',
                'Content-Length': chunksize,
                'Content-Type': 'application/octet-stream',
            });
        } else {
            res.setHeader('Content-Length', chunksize);
            res.setHeader('Content-Type', 'application/octet-stream');
            res.setHeader('Accept-Ranges', 'bytes');
        }
        fs.createReadStream(pkg.filePath, { start, end }).pipe(cipher).pipe(res);
    }

    async function downloadSignature(req, res) {
        const runtime = req.app.locals.runtime;
        const packageId = assertString(req.params.id, 'packageId');
        const token = assertString(req.query.token, 'token');
        const machineId = assertString(req.query.machineId, 'machineId');

        if (!isValidDeploymentUserAgent(req.get('user-agent'))) {
            res.status(403).json({ success: false, error: 'User-Agent 校验失败' });
            return;
        }

        const tokenRecord = await deploymentStore.validateToken(runtime.database, token, {
            machineId,
            packageId,
            resourceType: 'signature',
        });
        if (!tokenRecord) {
            res.status(403).json({ success: false, error: '下载令牌无效或已过期' });
            return;
        }

        const sigPath = deploymentStore.getSignatureFilePath(packageId);
        if (!fs.existsSync(sigPath)) {
            res.status(404).json({ success: false, error: '签名文件不存在' });
            return;
        }

        await deploymentStore.markTokenUsed(runtime.database, token);

        const stat = fs.statSync(sigPath);
        // 从 token 获取 AES 加密参数，对签名文件流做 AES-256-CTR 动态加密
        const aesKey = Buffer.from(tokenRecord.aes_key || '', 'base64');
        const aesIv = Buffer.from(tokenRecord.aes_iv || '', 'base64');
        const cipher = createCtrCipher(aesKey, aesIv, 0);

        res.setHeader('Content-Length', stat.size);
        res.setHeader('Content-Type', 'application/octet-stream');
        fs.createReadStream(sigPath).pipe(cipher).pipe(res);
    }

    return {
        uploadPackage,
        listPackages,
        getPackage,
        deletePackage,
        createTask,
        deleteTask,
        listTasks,
        getPendingTasks,
        reportTaskStatus,
        syncRecords,
        getMachineHistory,
        triggerUninstall,
        downloadPackage,
        downloadSignature,
        asyncHandler,
        requireAuth,
        requireDatabase,
    };
}

module.exports = { buildDeploymentRoutes, PACKAGE_MAX_BYTES };
