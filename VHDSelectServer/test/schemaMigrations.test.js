const assert = require('node:assert/strict');
const test = require('node:test');

const {
    getLatestSchemaVersion,
    loadSchemaMigrations,
    runSchemaMigrations,
} = require('../schemaMigrations');

function createFakeMigrationClient(appliedRows = []) {
    const state = {
        appliedRows: appliedRows.map((row) => ({ ...row })),
        executedMigrations: [],
        queryLog: [],
    };

    return {
        state,
        async query(sql, params = []) {
            const text = String(sql || '').trim();
            state.queryLog.push({ sql: text, params: [...params] });

            if (text.startsWith('CREATE TABLE IF NOT EXISTS schema_version')) {
                return { rows: [] };
            }

            if (text.startsWith('SELECT pg_advisory_lock') || text.startsWith('SELECT pg_advisory_unlock')) {
                return { rows: [] };
            }

            if (text.startsWith('SELECT version, name, checksum, applied_at')) {
                return {
                    rows: state.appliedRows
                        .slice()
                        .sort((left, right) => left.version - right.version),
                };
            }

            if (text === 'BEGIN' || text === 'COMMIT' || text === 'ROLLBACK') {
                return { rows: [] };
            }

            if (text.startsWith('UPDATE schema_version')) {
                const [version, name, checksum] = params;
                const row = state.appliedRows.find((entry) => entry.version === version);
                row.name = name;
                row.checksum = checksum;
                return { rows: [] };
            }

            if (text.startsWith('INSERT INTO schema_version')) {
                const [version, name, checksum] = params;
                state.appliedRows.push({ version, name, checksum, applied_at: new Date().toISOString() });
                return { rows: [] };
            }

            state.executedMigrations.push(text);
            return { rows: [] };
        },
    };
}

test('loadSchemaMigrations returns ordered migration files', () => {
    const migrations = loadSchemaMigrations();

    assert.ok(migrations.length >= 3);
    assert.deepEqual(
        migrations.map((migration) => migration.version),
        [1, 2, 3],
    );
    assert.equal(getLatestSchemaVersion(migrations), 3);
    migrations.forEach((migration) => {
        assert.match(migration.fileName, /^\d+_[a-z0-9_]+\.sql$/i);
        assert.match(migration.checksum, /^[a-f0-9]{64}$/);
        assert.ok(migration.sql.length > 0);
    });
});

test('runSchemaMigrations applies pending migrations in order', async () => {
    const client = createFakeMigrationClient();
    const logger = { info() {} };

    const result = await runSchemaMigrations(client, logger);

    assert.deepEqual(result.appliedVersions, [1, 2, 3]);
    assert.equal(result.latestVersion, 3);
    assert.equal(client.state.executedMigrations.length, 3);
    assert.deepEqual(
        client.state.appliedRows.map((row) => row.version),
        [1, 2, 3],
    );
});

test('runSchemaMigrations backfills missing checksum metadata without reapplying', async () => {
    const migrations = loadSchemaMigrations();
    const client = createFakeMigrationClient([
        { version: 1, name: 'initial_schema', checksum: null, applied_at: '2026-04-20T00:00:00.000Z' },
        { version: 2, name: 'machine_security_columns', checksum: migrations[1].checksum, applied_at: '2026-04-20T00:00:00.000Z' },
        { version: 3, name: 'machine_log_schema', checksum: migrations[2].checksum, applied_at: '2026-04-20T00:00:00.000Z' },
    ]);
    const logger = { info() {} };

    const result = await runSchemaMigrations(client, logger, { migrations });

    assert.deepEqual(result.appliedVersions, []);
    assert.deepEqual(result.metadataBackfilled, [1]);
    assert.equal(client.state.executedMigrations.length, 0);
    assert.equal(client.state.appliedRows[0].checksum, migrations[0].checksum);
});

test('runSchemaMigrations rejects checksum mismatches', async () => {
    const client = createFakeMigrationClient([
        { version: 1, name: 'initial_schema', checksum: 'deadbeef', applied_at: '2026-04-20T00:00:00.000Z' },
    ]);

    await assert.rejects(
        () => runSchemaMigrations(client, { info() {} }),
        /校验和不匹配/,
    );
});