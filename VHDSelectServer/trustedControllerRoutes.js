'use strict';

/**
 * 管理面：可信 RustDesk 主控端 admin 端点（任务 15.2 / Requirement 15.2 / 15.3）。
 *
 *  - GET    /api/security/trusted-rustdesk-controllers           列表
 *  - POST   /api/security/trusted-rustdesk-controllers           upsert
 *  - DELETE /api/security/trusted-rustdesk-controllers/:id       删除
 *
 * 沿用 server.js 中 /api/security/trusted-certificates 的 requireAuth + requireOtpStepUp
 * 中间件链；POST/DELETE 写审计 type='security.trusted-rustdesk-controller.add' /
 * 'security.trusted-rustdesk-controller.remove'。
 *
 * POST 时如果 scope == 'machine:<id>'，校验 machineId 在 machines 表存在，否则 400。
 *
 * 写操作触发 Wave 4 决策点 4 反向通知（Wave 3 走 noop，由
 * dispatchRevocationToMachine(machineId, reason) 占位）。
 *
 * 工厂 createTrustedControllerRoutes(deps) → { router }。
 * 不在本文件挂载到 server.js（Wave 4 任务 15.5）。
 */

const express = require('express');

const { dispatchRevocationToMachine } = require('./bridgeRoutes');

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function asyncHandler(handler) {
    return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
}

function createTrustedControllerRoutes(deps) {
    const {
        trustedControllerStore,
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        writeAudit,
    } = deps || {};
    if (!trustedControllerStore) throw new Error('createTrustedControllerRoutes: 缺少 trustedControllerStore');
    if (typeof requireAuth !== 'function') throw new Error('缺少 requireAuth 中间件');
    if (typeof requireOtpStepUp !== 'function') throw new Error('缺少 requireOtpStepUp 中间件');
    if (typeof requireDatabase !== 'function') throw new Error('缺少 requireDatabase 中间件');

    const auditOf = (req, fields) => {
        if (typeof writeAudit === 'function') writeAudit(req, fields);
    };

    const router = express.Router();

    router.get(
        '/trusted-rustdesk-controllers',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            await trustedControllerStore.ensureLoaded();
            const controllers = await trustedControllerStore.listAll();
            res.json({
                success: true,
                snapshotVersion: trustedControllerStore.currentSnapshotVersion(),
                controllers,
            });
        }),
    );

    router.post(
        '/trusted-rustdesk-controllers',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const body = req.body || {};
            const scope = String(body.scope || 'global').trim();

            if (scope !== 'global' && !scope.startsWith('machine:')) {
                throw createJsonError(400, 'scope 必须为 "global" 或 "machine:<machineId>"');
            }

            // scope == 'machine:<id>' 时校验 machineId 在 machines 表存在
            if (scope.startsWith('machine:')) {
                const targetMachineId = scope.substring('machine:'.length).trim();
                if (!targetMachineId || targetMachineId.length > 64) {
                    throw createJsonError(400, 'scope 中 machineId 不合法');
                }
                const runtime = req.app.locals.runtime;
                const machine = await runtime.database.getMachine(targetMachineId);
                if (!machine) {
                    throw createJsonError(400, `scope 引用的 machineId=${targetMachineId} 在 machines 表中不存在`);
                }
            }

            const record = await trustedControllerStore.upsert({
                id: body.id,
                controllerId: body.controllerId,
                controllerHwidHash: body.controllerHwidHash,
                label: body.label,
                scope,
                enabled: body.enabled,
                expiresAt: body.expiresAt,
                auditNote: body.auditNote,
            });

            auditOf(req, {
                type: 'security.trusted-rustdesk-controller.add',
                actor: 'admin',
                result: 'success',
                controllerId: record.controllerId,
                scope: record.scope,
                enabled: record.enabled,
                snapshotVersion: trustedControllerStore.currentSnapshotVersion(),
            });

            // 决策点 4 反向通知（Wave 3 占位）：global 推所有机台，machine:<id> 推单台
            try {
                if (record.scope === 'global') {
                    dispatchRevocationToMachine('*', 'denied');
                } else if (record.scope.startsWith('machine:')) {
                    dispatchRevocationToMachine(
                        record.scope.substring('machine:'.length),
                        'denied',
                    );
                }
            } catch (err) {
                // eslint-disable-next-line no-console
                console.warn('[trustedControllerRoutes] dispatchRevocationToMachine 失败（已忽略）:', err.message);
            }

            res.status(201).json({
                success: true,
                controller: record,
                snapshotVersion: trustedControllerStore.currentSnapshotVersion(),
            });
        }),
    );

    router.delete(
        '/trusted-rustdesk-controllers/:id',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const id = String(req.params.id || '').trim();
            if (!id) throw createJsonError(400, 'id 不能为空');

            // 删除前抓取一份记录用于反向通知
            let removedScope = null;
            try {
                const all = await trustedControllerStore.listAll();
                const target = all.find((c) => c.id === id);
                if (target) removedScope = target.scope;
            } catch {
                // 忽略 —— 反向通知是 best-effort
            }

            const removed = await trustedControllerStore.delete(id);
            if (!removed) {
                throw createJsonError(404, '可信主控端记录不存在');
            }

            auditOf(req, {
                type: 'security.trusted-rustdesk-controller.remove',
                actor: 'admin',
                result: 'success',
                controllerRecordId: id,
                snapshotVersion: trustedControllerStore.currentSnapshotVersion(),
            });

            try {
                if (removedScope === 'global') {
                    dispatchRevocationToMachine('*', 'denied');
                } else if (removedScope && removedScope.startsWith('machine:')) {
                    dispatchRevocationToMachine(
                        removedScope.substring('machine:'.length),
                        'denied',
                    );
                }
            } catch (err) {
                // eslint-disable-next-line no-console
                console.warn('[trustedControllerRoutes] dispatchRevocationToMachine 失败（已忽略）:', err.message);
            }

            res.json({
                success: true,
                snapshotVersion: trustedControllerStore.currentSnapshotVersion(),
            });
        }),
    );

    return { router };
}

module.exports = {
    createTrustedControllerRoutes,
};
