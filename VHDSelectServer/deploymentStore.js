const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const { ensureWritableDirectory } = require('./configStoreUtils');

const TOKEN_EXPIRY_MINUTES = 60;
const PACKAGES_SUBDIR = 'deployment-packages';

function generateId() {
    return crypto.randomBytes(16).toString('hex');
}

function generateToken() {
    return crypto.randomBytes(32).toString('hex');
}

class DeploymentStore {
    constructor(configDir = process.env.CONFIG_PATH || __dirname) {
        this.configDir = configDir;
        this.packagesDir = path.join(configDir, PACKAGES_SUBDIR);
        ensureWritableDirectory(this.packagesDir);
    }

    // ---------- 部署包 ----------

    async createPackage(database, {
        name, version, type, signer, fileName, fileSize,
    }) {
        const packageId = `pkg-${generateId()}`;
        const filePath = path.join(this.packagesDir, `${packageId}.zip`);

        const result = await database.withClient(async (client) => {
            const res = await client.query(`
                INSERT INTO deployment_packages (package_id, name, version, type, signer, file_path, file_size, expires_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, NOW() + INTERVAL '30 days')
                RETURNING *
            `, [packageId, name, version, type, signer, filePath, fileSize]);
            return res.rows[0];
        });

        return {
            packageId: result.package_id,
            name: result.name,
            version: result.version,
            type: result.type,
            signer: result.signer,
            filePath: result.file_path,
            fileSize: Number(result.file_size),
            createdAt: result.created_at,
            expiresAt: result.expires_at,
        };
    }

    async getPackage(database, packageId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_packages WHERE package_id = $1
            `, [packageId]);
            return this._mapPackageRow(result.rows[0]);
        });
    }

    async listPackages(database) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_packages ORDER BY created_at DESC
            `);
            return result.rows.map((row) => this._mapPackageRow(row));
        });
    }

    async deletePackage(database, packageId) {
        const pkg = await this.getPackage(database, packageId);
        if (!pkg) return null;

        await database.withClient(async (client) => {
            await client.query('DELETE FROM deployment_tasks WHERE package_id = $1', [packageId]);
            await client.query('DELETE FROM deployment_packages WHERE package_id = $1', [packageId]);
        });

        try {
            if (fs.existsSync(pkg.filePath)) fs.unlinkSync(pkg.filePath);
            const sigPath = `${pkg.filePath}.sig`;
            if (fs.existsSync(sigPath)) fs.unlinkSync(sigPath);
        } catch { }

        return pkg;
    }

    _mapPackageRow(row) {
        if (!row) return null;
        return {
            packageId: row.package_id,
            name: row.name,
            version: row.version,
            type: row.type,
            signer: row.signer,
            filePath: row.file_path,
            fileSize: Number(row.file_size),
            createdAt: row.created_at,
            expiresAt: row.expires_at,
        };
    }

    // ---------- 部署任务 ----------

    async createTask(database, { packageId, machineId, taskType = 'deploy', scheduledAt = null }) {
        const taskId = `task-${generateId()}`;

        const result = await database.withClient(async (client) => {
            const res = await client.query(`
                INSERT INTO deployment_tasks (task_id, package_id, machine_id, task_type, status, scheduled_at)
                VALUES ($1, $2, $3, $4, 'pending', $5)
                RETURNING *
            `, [taskId, packageId, machineId, taskType, scheduledAt]);
            return res.rows[0];
        });

        return this._mapTaskRow(result);
    }

    async getTask(database, taskId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_tasks WHERE task_id = $1
            `, [taskId]);
            return this._mapTaskRow(result.rows[0]);
        });
    }

    async listTasks(database, { machineId, status } = {}) {
        const conditions = [];
        const values = [];

        if (machineId) {
            values.push(machineId);
            conditions.push(`machine_id = $${values.length}`);
        }
        if (status) {
            values.push(status);
            conditions.push(`status = $${values.length}`);
        }

        const whereClause = conditions.length ? `WHERE ${conditions.join(' AND ')}` : '';

        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_tasks ${whereClause} ORDER BY created_at DESC
            `, values);
            return result.rows.map((row) => this._mapTaskRow(row));
        });
    }

    async listPendingTasks(database, machineId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT t.*, p.name, p.version, p.type, p.file_size
                FROM deployment_tasks t
                JOIN deployment_packages p ON t.package_id = p.package_id
                WHERE t.machine_id = $1 AND t.status = 'pending'
                  AND (t.scheduled_at IS NULL OR t.scheduled_at <= NOW())
                ORDER BY t.created_at ASC
            `, [machineId]);
            return result.rows.map((row) => ({
                ...this._mapTaskRow(row),
                packageName: row.name,
                packageVersion: row.version,
                packageType: row.type,
                packageSize: Number(row.file_size),
            }));
        });
    }

    async updateTaskStatus(database, taskId, status, errorMessage = null) {
        const updates = ['status = $2'];
        const values = [taskId, status];

        if (status === 'running') {
            updates.push('started_at = NOW()');
        }
        if (['success', 'failed'].includes(status)) {
            updates.push('completed_at = NOW()');
        }
        if (errorMessage) {
            values.push(errorMessage);
            updates.push(`error_message = $${values.length}`);
        }

        return database.withClient(async (client) => {
            const result = await client.query(`
                UPDATE deployment_tasks
                SET ${updates.join(', ')}
                WHERE task_id = $1
                RETURNING *
            `, values);
            return this._mapTaskRow(result.rows[0]);
        });
    }

    async deleteTask(database, taskId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                DELETE FROM deployment_tasks WHERE task_id = $1 RETURNING *
            `, [taskId]);
            return this._mapTaskRow(result.rows[0]);
        });
    }

    _mapTaskRow(row) {
        if (!row) return null;
        return {
            taskId: row.task_id,
            packageId: row.package_id,
            machineId: row.machine_id,
            taskType: row.task_type,
            status: row.status,
            scheduledAt: row.scheduled_at,
            startedAt: row.started_at,
            completedAt: row.completed_at,
            errorMessage: row.error_message,
            createdAt: row.created_at,
        };
    }

    // ---------- 临时下载令牌 ----------

    async createDownloadToken(database, { taskId, machineId, packageId, resourceType, aesKey = null, aesIv = null }) {
        const token = generateToken();

        await database.withClient(async (client) => {
            await client.query(`
                INSERT INTO deployment_tokens (token, task_id, machine_id, package_id, resource_type, expires_at, aes_key, aes_iv)
                VALUES ($1, $2, $3, $4, $5, NOW() + INTERVAL '${TOKEN_EXPIRY_MINUTES} minutes', $6, $7)
            `, [token, taskId, machineId, packageId, resourceType, aesKey, aesIv]);
        });

        return token;
    }

    async validateToken(database, token, { machineId, packageId, resourceType }) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_tokens
                WHERE token = $1 AND machine_id = $2 AND package_id = $3 AND resource_type = $4
                  AND expires_at > NOW()
            `, [token, machineId, packageId, resourceType]);
            return result.rows[0] || null;
        });
    }

    async markTokenUsed(database, token) {
        await database.withClient(async (client) => {
            await client.query(`
                UPDATE deployment_tokens SET used_at = NOW() WHERE token = $1
            `, [token]);
        });
    }

    async cleanupExpiredTokens(database) {
        await database.withClient(async (client) => {
            const result = await client.query(`
                DELETE FROM deployment_tokens WHERE expires_at < NOW() - INTERVAL '24 hours'
            `);
            return result.rowCount;
        });
    }

    // ---------- 机台部署记录 ----------

    async syncDeploymentRecord(database, record) {
        const {
            recordId, machineId, packageId, name, version, type,
            targetPath, status, deployedAt, uninstalledAt,
        } = record;

        return database.withClient(async (client) => {
            const result = await client.query(`
                INSERT INTO deployment_records
                    (record_id, machine_id, package_id, name, version, type, target_path, status, deployed_at, uninstalled_at, synced_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, NOW())
                ON CONFLICT (record_id) DO UPDATE SET
                    status = EXCLUDED.status,
                    uninstalled_at = EXCLUDED.uninstalled_at,
                    synced_at = NOW()
                RETURNING *
            `, [recordId, machineId, packageId, name, version, type, targetPath, status, deployedAt, uninstalledAt]);
            return result.rows[0];
        });
    }

    async listMachineRecords(database, machineId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_records WHERE machine_id = $1 ORDER BY deployed_at DESC
            `, [machineId]);
            return result.rows.map((row) => this._mapRecordRow(row));
        });
    }

    async getRecordById(database, recordId) {
        return database.withClient(async (client) => {
            const result = await client.query(`
                SELECT * FROM deployment_records WHERE record_id = $1
            `, [recordId]);
            return this._mapRecordRow(result.rows[0]);
        });
    }

    _mapRecordRow(row) {
        if (!row) return null;
        return {
            recordId: row.record_id,
            machineId: row.machine_id,
            packageId: row.package_id,
            name: row.name,
            version: row.version,
            type: row.type,
            targetPath: row.target_path,
            status: row.status,
            deployedAt: row.deployed_at,
            uninstalledAt: row.uninstalled_at,
            syncedAt: row.synced_at,
        };
    }

    // ---------- 文件存储 ----------

    getPackageFilePath(packageId) {
        return path.join(this.packagesDir, `${packageId}.zip`);
    }

    getSignatureFilePath(packageId) {
        return path.join(this.packagesDir, `${packageId}.zip.sig`);
    }
}

module.exports = { DeploymentStore };
