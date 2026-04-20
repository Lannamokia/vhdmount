const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const SCHEMA_VERSION_TABLE = 'schema_version';
const SCHEMA_MIGRATION_LOCK_ID = 2026042001;
const MIGRATIONS_DIR = path.join(__dirname, 'migrations');

let cachedSchemaMigrations = null;

function computeMigrationChecksum(sql) {
    return crypto.createHash('sha256').update(sql, 'utf8').digest('hex');
}

function parseMigrationFile(fileName, migrationsDir = MIGRATIONS_DIR) {
    const match = /^(\d+)_([a-z0-9_]+)\.sql$/i.exec(fileName);
    if (!match) {
        return null;
    }

    const version = Number(match[1]);
    if (!Number.isSafeInteger(version) || version <= 0) {
        throw new Error(`非法迁移版本号: ${fileName}`);
    }

    const filePath = path.join(migrationsDir, fileName);
    const sql = fs.readFileSync(filePath, 'utf8').replace(/\r\n/g, '\n').trim();
    if (!sql) {
        throw new Error(`迁移文件为空: ${fileName}`);
    }

    return Object.freeze({
        version,
        name: match[2],
        fileName,
        filePath,
        sql,
        checksum: computeMigrationChecksum(sql),
    });
}

function validateSchemaMigrations(migrations) {
    if (migrations.length === 0) {
        return migrations;
    }

    const seen = new Set();
    migrations.forEach((migration, index) => {
        if (seen.has(migration.version)) {
            throw new Error(`发现重复的 schema migration 版本: ${migration.version}`);
        }
        seen.add(migration.version);

        const expectedVersion = index + 1;
        if (migration.version !== expectedVersion) {
            throw new Error(`schema migration 版本必须从 1 开始连续递增，期望 ${expectedVersion}，实际 ${migration.version}`);
        }
    });

    return migrations;
}

function loadSchemaMigrations(migrationsDir = MIGRATIONS_DIR) {
    if (migrationsDir === MIGRATIONS_DIR && cachedSchemaMigrations) {
        return cachedSchemaMigrations;
    }

    const migrations = fs.readdirSync(migrationsDir)
        .map((fileName) => parseMigrationFile(fileName, migrationsDir))
        .filter(Boolean)
        .sort((left, right) => left.version - right.version);

    const validated = Object.freeze(validateSchemaMigrations(migrations).slice());
    if (migrationsDir === MIGRATIONS_DIR) {
        cachedSchemaMigrations = validated;
    }
    return validated;
}

function getLatestSchemaVersion(migrations = loadSchemaMigrations()) {
    return migrations.length > 0 ? migrations[migrations.length - 1].version : 0;
}

async function ensureSchemaVersionTable(client) {
    await client.query(`
        CREATE TABLE IF NOT EXISTS ${SCHEMA_VERSION_TABLE} (
            version INTEGER PRIMARY KEY,
            name VARCHAR(255) NOT NULL,
            checksum VARCHAR(64) DEFAULT NULL,
            applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    `);
}

async function listAppliedSchemaVersions(client) {
    const result = await client.query(`
        SELECT version, name, checksum, applied_at
        FROM ${SCHEMA_VERSION_TABLE}
        ORDER BY version ASC
    `);

    return result.rows.map((row) => ({
        version: Number(row.version),
        name: String(row.name || '').trim(),
        checksum: row.checksum ? String(row.checksum).trim() : null,
        applied_at: row.applied_at || null,
    }));
}

async function syncAppliedMigrationMetadata(client, migration, appliedRow) {
    if (appliedRow.name === migration.name && appliedRow.checksum === migration.checksum) {
        return false;
    }

    await client.query(`
        UPDATE ${SCHEMA_VERSION_TABLE}
        SET name = $2,
            checksum = $3
        WHERE version = $1
    `, [migration.version, migration.name, migration.checksum]);

    return true;
}

async function applySchemaMigration(client, migration, logger = console) {
    logger.info(`应用 schema migration v${migration.version}: ${migration.fileName}`);
    await client.query('BEGIN');
    try {
        await client.query(migration.sql);
        await client.query(`
            INSERT INTO ${SCHEMA_VERSION_TABLE} (version, name, checksum, applied_at)
            VALUES ($1, $2, $3, CURRENT_TIMESTAMP)
        `, [migration.version, migration.name, migration.checksum]);
        await client.query('COMMIT');
    } catch (error) {
        await client.query('ROLLBACK');
        throw new Error(`应用 schema migration v${migration.version} 失败: ${error.message}`);
    }
}

async function runSchemaMigrations(client, logger = console, options = {}) {
    const migrations = options.migrations || loadSchemaMigrations(options.migrationsDir || MIGRATIONS_DIR);
    await ensureSchemaVersionTable(client);
    await client.query('SELECT pg_advisory_lock($1)', [SCHEMA_MIGRATION_LOCK_ID]);

    try {
        await ensureSchemaVersionTable(client);

        const appliedRows = await listAppliedSchemaVersions(client);
        const migrationByVersion = new Map(migrations.map((migration) => [migration.version, migration]));
        const appliedByVersion = new Map();
        const metadataBackfilled = [];

        for (const appliedRow of appliedRows) {
            const migration = migrationByVersion.get(appliedRow.version);
            if (!migration) {
                throw new Error(`数据库包含未知的 schema_version ${appliedRow.version}，当前程序无法安全启动`);
            }

            if (appliedRow.checksum && appliedRow.checksum !== migration.checksum) {
                throw new Error(`schema_version ${appliedRow.version} 的迁移校验和不匹配，请确认迁移文件未被篡改`);
            }

            if (await syncAppliedMigrationMetadata(client, migration, appliedRow)) {
                metadataBackfilled.push(migration.version);
            }

            appliedByVersion.set(migration.version, migration);
        }

        const appliedVersions = [];
        for (const migration of migrations) {
            if (appliedByVersion.has(migration.version)) {
                continue;
            }

            await applySchemaMigration(client, migration, logger);
            appliedVersions.push(migration.version);
        }

        if (metadataBackfilled.length > 0) {
            logger.info(`已回填 schema_version 元数据: ${metadataBackfilled.join(', ')}`);
        }

        return {
            latestVersion: getLatestSchemaVersion(migrations),
            appliedVersions,
            metadataBackfilled,
        };
    } finally {
        await client.query('SELECT pg_advisory_unlock($1)', [SCHEMA_MIGRATION_LOCK_ID]);
    }
}

module.exports = {
    MIGRATIONS_DIR,
    SCHEMA_VERSION_TABLE,
    applySchemaMigration,
    ensureSchemaVersionTable,
    getLatestSchemaVersion,
    listAppliedSchemaVersions,
    loadSchemaMigrations,
    runSchemaMigrations,
};