'use strict';

const crypto = require('crypto');

/**
 * Report 上行存储（任务 14.5 / Requirement 15.1 / 15.9）。
 *
 * 服务端用 K（wrap_key）解密机台上行的 password 密文得到明文后，把：
 *   - password_plaintext         以列级加密（与既有部署密码同等强度）形式落盘
 *   - password_hash_prefix       sha256(plaintext)[..8] 作为审计中指代密码用的短哈希
 * 写入 rustdesk_reports（machine_id 主键，每机台一行 upsert）。
 *
 * 与 securityStore.js / wrapKeyStore.js 风格一致：构造函数注入 database / logger，
 * 不持有任何长寿命密码字节。
 */
class ReportStore {
    constructor(database, logger = console) {
        this.database = database;
        this.logger = logger;
    }

    /**
     * upsertReport({ machineId, rustDeskId, passwordKind, passwordPlaintext, lastWrapKeyId, secretVersion, reportedAt })
     *
     * - passwordKind == 'absent' 时 passwordPlaintext 必须是空字符串 ''；plaintext 列写空串、hash 列写 null
     * - 否则 plaintext 列原样写入；hash 列写 sha256(plaintext) 前 8 位 hex
     */
    async upsertReport(input) {
        const normalized = normalizeReportInput(input);

        return this.database.withTransaction(async (client) => {
            const result = await client.query(`
                INSERT INTO rustdesk_reports
                    (machine_id, rust_desk_id, password_kind, password_plaintext,
                     password_hash_prefix, last_wrap_key_id, secret_version,
                     reported_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW())
                ON CONFLICT (machine_id) DO UPDATE SET
                    rust_desk_id        = EXCLUDED.rust_desk_id,
                    password_kind       = EXCLUDED.password_kind,
                    password_plaintext  = EXCLUDED.password_plaintext,
                    password_hash_prefix = EXCLUDED.password_hash_prefix,
                    last_wrap_key_id    = EXCLUDED.last_wrap_key_id,
                    secret_version      = EXCLUDED.secret_version,
                    reported_at         = EXCLUDED.reported_at,
                    updated_at          = NOW()
                RETURNING machine_id, rust_desk_id, password_kind, password_hash_prefix,
                          last_wrap_key_id, secret_version, reported_at, updated_at
            `, [
                normalized.machineId,
                normalized.rustDeskId,
                normalized.passwordKind,
                normalized.passwordPlaintext,
                normalized.passwordHashPrefix,
                normalized.lastWrapKeyId,
                normalized.secretVersion,
                normalized.reportedAt,
            ]);
            return mapRow(result.rows[0]);
        });
    }

    /**
     * 单机台读取（不返回 plaintext）—— 给管理面可视化用。
     * 明文密码读取走独立 admin endpoint + OTP step-up（与 EVHD 密码一致）。
     */
    async getReportSummary(machineId) {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT machine_id, rust_desk_id, password_kind, password_hash_prefix,
                       last_wrap_key_id, secret_version, reported_at, updated_at
                FROM rustdesk_reports
                WHERE machine_id = $1
            `, [String(machineId || '').trim()]);
            return result.rows.length === 0 ? null : mapRow(result.rows[0]);
        });
    }

    /**
     * 仅在 OTP step-up 通过后由 routes 层调用：返回明文密码。
     * 调用方负责审计 + 限流；本方法只是数据读取。
     */
    async getReportPlaintext(machineId) {
        return this.database.withClient(async (client) => {
            const result = await client.query(`
                SELECT machine_id, rust_desk_id, password_kind, password_plaintext,
                       password_hash_prefix, last_wrap_key_id, secret_version, reported_at
                FROM rustdesk_reports
                WHERE machine_id = $1
            `, [String(machineId || '').trim()]);
            if (result.rows.length === 0) return null;
            const row = result.rows[0];
            return {
                ...mapRow(row),
                passwordPlaintext: row.password_plaintext === null ? '' : String(row.password_plaintext),
            };
        });
    }
}

function normalizeReportInput(input) {
    if (!input || typeof input !== 'object') {
        throw new Error('upsertReport 输入不能为空');
    }

    const machineId = String(input.machineId || '').trim();
    if (!machineId) throw new Error('machineId 不能为空');

    const rustDeskId = String(input.rustDeskId || '').trim();
    if (!rustDeskId) throw new Error('rustDeskId 不能为空');

    const passwordKind = String(input.passwordKind || '').trim();
    if (!['temporary', 'permanent', 'preset', 'absent'].includes(passwordKind)) {
        throw new Error(`passwordKind 非法: ${passwordKind}`);
    }

    const rawPlaintext = input.passwordPlaintext === undefined || input.passwordPlaintext === null
        ? ''
        : String(input.passwordPlaintext);

    if (passwordKind === 'absent' && rawPlaintext !== '') {
        throw new Error('passwordKind == "absent" 时 passwordPlaintext 必须为空字符串');
    }

    const passwordPlaintext = rawPlaintext;
    const passwordHashPrefix = passwordKind === 'absent' || passwordPlaintext === ''
        ? null
        : crypto.createHash('sha256').update(passwordPlaintext, 'utf8').digest('hex').slice(0, 8);

    const lastWrapKeyId = input.lastWrapKeyId === undefined || input.lastWrapKeyId === null
        ? null
        : String(input.lastWrapKeyId).trim() || null;

    const secretVersion = input.secretVersion === undefined || input.secretVersion === null
        ? null
        : Number(input.secretVersion);
    if (secretVersion !== null && (!Number.isFinite(secretVersion) || secretVersion < 0 || secretVersion > 4294967295)) {
        throw new Error('secretVersion 越界');
    }

    let reportedAt;
    if (input.reportedAt instanceof Date) {
        reportedAt = input.reportedAt.toISOString();
    } else if (typeof input.reportedAt === 'number' && Number.isFinite(input.reportedAt)) {
        reportedAt = new Date(input.reportedAt).toISOString();
    } else if (typeof input.reportedAt === 'string' && input.reportedAt.trim()) {
        const parsed = new Date(input.reportedAt);
        if (Number.isNaN(parsed.getTime())) {
            throw new Error('reportedAt 不是合法时间');
        }
        reportedAt = parsed.toISOString();
    } else {
        throw new Error('reportedAt 不能为空');
    }

    return {
        machineId,
        rustDeskId,
        passwordKind,
        passwordPlaintext,
        passwordHashPrefix,
        lastWrapKeyId,
        secretVersion,
        reportedAt,
    };
}

function mapRow(row) {
    if (!row) return null;
    return {
        machineId: row.machine_id,
        rustDeskId: row.rust_desk_id,
        passwordKind: row.password_kind,
        passwordHashPrefix: row.password_hash_prefix || null,
        lastWrapKeyId: row.last_wrap_key_id || null,
        secretVersion: row.secret_version === null || row.secret_version === undefined
            ? null
            : Number(row.secret_version),
        reportedAt: row.reported_at instanceof Date
            ? row.reported_at.toISOString()
            : (row.reported_at || null),
        updatedAt: row.updated_at instanceof Date
            ? row.updated_at.toISOString()
            : (row.updated_at || null),
    };
}

module.exports = {
    ReportStore,
    normalizeReportInput,
    mapRow,
};
