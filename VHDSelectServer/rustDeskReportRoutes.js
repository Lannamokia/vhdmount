'use strict';

/**
 * 管理面：RustDesk 上报记录 admin 端点。
 *
 *  - GET /api/security/rustdesk-reports
 *      列出全部机台最近一条上报（不返回明文密码）。仅需 requireAuth + requireOtpStepUp
 *      （与 trusted-certificates / bridge-secret 链一致）。
 *
 *  - GET /api/security/rustdesk-reports/:machineId/plaintext?reason=...
 *      返回单台机台的明文密码 + 完整摘要。requireAuth + requireOtpStepUp，
 *      并写 type='security.rustdesk-report.read' 审计行（白名单字段：
 *      machineId / rustDeskId / passwordKind / hashPrefix / reason，
 *      绝不写明文）。reason 为必填，与 /api/evhd-password/plain 一致。
 *
 * 工厂 createRustDeskReportRoutes(deps) → { router }；deps 沿用 buildBridgeRouteDeps
 * 已经构造的对象（reportStore / requireAuth / requireOtpStepUp / requireDatabase /
 * writeAudit）。
 *
 * 此文件**不**让机台访问；机台上报走 bridgeRoutes.js 的
 * POST /api/machines/:machineId/rustdesk/report，与本文件互不耦合。
 */

const express = require('express');

function createJsonError(statusCode, message, extra = {}) {
    const error = new Error(message);
    error.statusCode = statusCode;
    Object.assign(error, extra);
    return error;
}

function asyncHandler(handler) {
    return (req, res, next) => Promise.resolve(handler(req, res, next)).catch(next);
}

function assertMachineIdLike(input) {
    const value = String(input == null ? '' : input).trim();
    if (!value) {
        throw createJsonError(400, 'machineId 不能为空');
    }
    if (value.length > 128) {
        throw createJsonError(400, 'machineId 过长');
    }
    if (!/^[A-Za-z0-9_.\-:]+$/.test(value)) {
        throw createJsonError(400, 'machineId 含非法字符');
    }
    return value;
}

function assertReason(input) {
    const value = String(input == null ? '' : input).trim();
    if (!value) {
        throw createJsonError(400, 'reason 不能为空');
    }
    if (value.length > 256) {
        throw createJsonError(400, 'reason 过长');
    }
    return value;
}

function createRustDeskReportRoutes(deps) {
    const {
        reportStore,
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        writeAudit,
    } = deps || {};
    if (!reportStore) throw new Error('createRustDeskReportRoutes: 缺少 reportStore');
    if (typeof requireAuth !== 'function') throw new Error('缺少 requireAuth 中间件');
    if (typeof requireOtpStepUp !== 'function') throw new Error('缺少 requireOtpStepUp 中间件');
    if (typeof requireDatabase !== 'function') throw new Error('缺少 requireDatabase 中间件');

    const auditOf = (req, fields) => {
        if (typeof writeAudit === 'function') writeAudit(req, fields);
    };

    const router = express.Router();

    // ---------- GET /api/security/rustdesk-reports ----------
    //
    // 列出每台机台最近一条上报摘要（不含明文）。reportStore 暴露了 listAll；
    // 若旧实例没有 listAll，则降级用 SQL 直读 —— 这里通过 reportStore.listAll
    // 表达，新增一个方法（见 reportStore.js 同 commit）。
    router.get(
        '/rustdesk-reports',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const reports = typeof reportStore.listAll === 'function'
                ? await reportStore.listAll()
                : [];
            res.json({
                success: true,
                reports,
            });
        }),
    );

    // ---------- GET /api/security/rustdesk-reports/:machineId/plaintext ----------
    //
    // 返回单机明文密码。绝不在 URL / 响应 / 审计行任何位置带 plaintext —— 仅响应
    // 体的 passwordPlaintext 字段；审计行字段白名单与 /api/security/rustdesk-reports
    // 列表保持一致。
    router.get(
        '/rustdesk-reports/:machineId/plaintext',
        requireAuth,
        requireOtpStepUp,
        requireDatabase,
        asyncHandler(async (req, res) => {
            const machineId = assertMachineIdLike(req.params.machineId);
            const reason = assertReason(req.query.reason);

            const record = await reportStore.getReportPlaintext(machineId);
            if (!record) {
                auditOf(req, {
                    type: 'security.rustdesk-report.read',
                    actor: 'admin',
                    result: 'not_found',
                    machineId,
                    reason,
                });
                throw createJsonError(404, '该机台暂无 RustDesk 上报记录');
            }

            auditOf(req, {
                type: 'security.rustdesk-report.read',
                actor: 'admin',
                result: 'success',
                machineId,
                rustDeskId: record.rustDeskId,
                passwordKind: record.passwordKind,
                passwordHashPrefix: record.passwordHashPrefix,
                secretVersion: record.secretVersion,
                reason,
            });

            res.json({
                success: true,
                report: {
                    machineId: record.machineId,
                    rustDeskId: record.rustDeskId,
                    passwordKind: record.passwordKind,
                    passwordPlaintext: record.passwordPlaintext || '',
                    passwordHashPrefix: record.passwordHashPrefix,
                    lastWrapKeyId: record.lastWrapKeyId,
                    secretVersion: record.secretVersion,
                    reportedAt: record.reportedAt,
                    updatedAt: record.updatedAt || null,
                },
            });
        }),
    );

    return { router };
}

module.exports = {
    createRustDeskReportRoutes,
};
