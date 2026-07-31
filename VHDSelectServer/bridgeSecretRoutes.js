'use strict';

/**
 * 管理面：RustDeskClientSharedSecret 录入端点（任务 15.3 / Requirement 13.1）。
 *
 *  - GET  /api/security/rustdesk-bridge-secret             列出版本元数据（不返回 keyMaterial）
 *  - POST /api/security/rustdesk-bridge-secret             录入新版本 + 自动激活
 *
 * 沿用 server.js 中 /api/security/trusted-certificates 的 requireAuth + requireOtpStepUp
 * 中间件链。POST 写审计 type='security.rustdesk-bridge-secret.activate'。
 *
 * POST body 形如 { format: 'hex'|'base64'|'binary', keyMaterialBase64: '<base64 of 32 bytes>',
 *   auditNote?: string }。无论传入哪种 format，前端必须先把字节解码为 base64，再发到本路由 ——
 * 服务端只信任 base64 解码恰好 32 字节的 keyMaterialBase64 字段。
 *
 * 录入成功后触发反向通知 dispatchRevocationToMachine('*', 'secret_outdated')，让所有机台
 * 立即丢弃旧 secretVersion 的 Snapshot 并重新拉取（决策点 4 占位）。
 *
 * 工厂 createBridgeSecretRoutes(deps) → { router }。
 */

const express = require('express');

const { dispatchRevocationToMachine } = require('./bridgeRoutes');

const ALLOWED_FORMATS = new Set(['hex', 'base64', 'binary']);

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function asyncHandler(handler) {
    return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
}

function createBridgeSecretRoutes(deps) {
    const {
        bridgeSecretStore,
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        writeAudit,
    } = deps || {};
    if (!bridgeSecretStore) throw new Error('createBridgeSecretRoutes: 缺少 bridgeSecretStore');
    if (typeof requireAuth !== 'function') throw new Error('缺少 requireAuth 中间件');
    if (typeof requireOtpStepUp !== 'function') throw new Error('缺少 requireOtpStepUp 中间件');
    if (typeof requireDatabase !== 'function') throw new Error('缺少 requireDatabase 中间件');

    const auditOf = (req, fields) => {
        if (typeof writeAudit === 'function') writeAudit(req, fields);
    };

    const router = express.Router();

    router.get(
        '/rustdesk-bridge-secret',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const versions = await bridgeSecretStore.listVersions();
            res.json({ success: true, versions });
        }),
    );

    router.post(
        '/rustdesk-bridge-secret',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const body = req.body || {};
            const format = String(body.format || '').trim().toLowerCase();
            const keyMaterialBase64 = String(body.keyMaterialBase64 || '').trim();
            const auditNote = body.auditNote == null ? null : String(body.auditNote).trim().slice(0, 1024);

            if (!ALLOWED_FORMATS.has(format)) {
                throw createJsonError(400, 'format 必须为 hex / base64 / binary 之一');
            }
            if (!keyMaterialBase64) {
                throw createJsonError(400, 'keyMaterialBase64 不能为空');
            }

            let keyMaterial;
            try {
                keyMaterial = Buffer.from(keyMaterialBase64, 'base64');
            } catch {
                throw createJsonError(400, 'keyMaterialBase64 解码失败');
            }
            if (keyMaterial.length !== 32) {
                throw createJsonError(400, 'RustDeskClientSharedSecret 必须正好 32 字节');
            }

            const createdByUserId = req.session?.userId || req.session?.adminId || null;
            const inserted = await bridgeSecretStore.insertAndActivate({
                keyMaterial,
                createdByUserId,
                auditNote,
            });

            auditOf(req, {
                type: 'security.rustdesk-bridge-secret.activate',
                actor: 'admin',
                result: 'success',
                secretVersion: inserted.secretVersion,
                inputFormat: format,
            });

            // 录入成功 → 立即让所有机台失效旧 Snapshot 并刷新 secret。
            try {
                dispatchRevocationToMachine('*', 'secret_outdated');
            } catch (err) {
                // eslint-disable-next-line no-console
                console.warn('[bridgeSecretRoutes] dispatchRevocationToMachine 失败（已忽略）:', err.message);
            }

            res.status(201).json({
                success: true,
                version: inserted,
            });
        }),
    );

    return { router };
}

module.exports = {
    createBridgeSecretRoutes,
};
