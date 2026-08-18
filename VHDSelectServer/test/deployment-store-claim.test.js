const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const test = require('node:test');

const { DeploymentStore } = require('../deploymentStore');

function createClaimHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'deployment-store-claim-'));
    t.after(() => fs.rmSync(tempDir, { recursive: true, force: true }));

    const calls = [];
    const client = {
        async query(sql, params = []) {
            calls.push({ sql, params });
            return { rows: [] };
        },
    };
    const database = {
        async withTransaction(work) {
            return work(client);
        },
    };

    return {
        store: new DeploymentStore(tempDir),
        database,
        calls,
    };
}

test('claimPendingTasks 默认类型查询只绑定 machineId', async (t) => {
    const { store, database, calls } = createClaimHarness(t);

    await store.claimPendingTasks(database, 'machine-001', {
        leaseDurationSeconds: 1800,
    });

    assert.strictEqual(calls.length, 1);
    assert.deepStrictEqual(calls[0].params, ['machine-001']);
    assert.match(calls[0].sql, /p\.type IN \('software-deploy', 'file-deploy'\)/);
});

test('claimPendingTasks 指定类型查询连续使用 $1 和 $2', async (t) => {
    const { store, database, calls } = createClaimHarness(t);

    await store.claimPendingTasks(database, 'machine-001', {
        leaseDurationSeconds: 1800,
        packageType: 'game-option-deploy',
    });

    assert.strictEqual(calls.length, 1);
    assert.deepStrictEqual(calls[0].params, ['machine-001', 'game-option-deploy']);
    assert.match(calls[0].sql, /p\.type = \$2/);
    assert.doesNotMatch(calls[0].sql, /\$3/);
});
