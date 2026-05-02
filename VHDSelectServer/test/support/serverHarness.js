const assert = require('node:assert/strict');
const crypto = require('crypto');
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');

const { authenticator } = require('otplib');
const request = require('supertest');
const { WebSocket } = require('ws');

const {
    buildMachineLogHelloSigningPayload,
    buildRegistrationSigningPayload,
} = require('../../registrationAuth');
const {
    attachMachineLogWebSocketServer,
    MACHINE_LOG_PROTOCOL_VERSION,
} = require('../../machineLogServer');
const { createApp } = require('../../server');

const TEST_REGISTRATION_CERT_PEM = `-----BEGIN CERTIFICATE-----
MIICzzCCAbegAwIBAgIJAPRk63P6tbNBMA0GCSqGSIb3DQEBCwUAMCcxJTAjBgNV
BAMTHFZIRE1vdW50IFRlc3QgUmVnaXN0cmF0aW9uIDIwHhcNMjYwNDAyMDgxOTU2
WhcNMjcwNDAzMDgxOTU2WjAnMSUwIwYDVQQDExxWSERNb3VudCBUZXN0IFJlZ2lz
dHJhdGlvbiAyMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqQ2RkyOZ
eN5wnvv1lXuISybOJFbjkSBnAorJj65LaKwooaptr3ySqeLSkxIA+5o6Rzgw+lh/
CAJhAcagfDAQ/2aH05dNUAoMO6smFJKFovslCBog7bk+vh+XoPs9jjsqT0gVzjH4
ncbq8e2diDrkcSdiRhSajxmrwnKywU3dKNqWm6NKyRQogf9f5kOJ//B7jVGC5yst
Mi3h9LHNWAXolich5vuOgZOrDxi5V5KDWXXsVVvtvwtVNg86cw83EHOayiU5uEEv
lmVmmLXjTC+0hKAXOsRMMfdkOFIdICmeLaiLRMAsM9ylxDOKB/n2FsbQLRF702jZ
CckgI7ncjew57QIDAQABMA0GCSqGSIb3DQEBCwUAA4IBAQAM0gG5dQ4dXTdxuWis
D5PMbJXGp6o6nqevH+RDR0Zk5vlbi7C/uhuOv/us9L+4YJbA3U5qHAKVevLZhC/A
Yv15STBGBP9dksHw2m0027tapgC3+GGzshbYZuYh5zIw2c9sNvdyA/tB4bJEKpae
NWV705ABr+DO/95GxssAmNdjJE4/cBmudREbx567NqHVn1q/dHvWM5jaVHxsSX8S
vYv27sFi6gn7xMgow34qN9arZkd5srj77GoQ/ycMClJsMVaRgmUqLuoei+QRlB64
6JYWszfC/N7haZFUpj31jjbs80Rw5JXrKo4XJ4T+BTiVwtaakK4rLu2En/beJpro
ZDNH
-----END CERTIFICATE-----`;

const TEST_REGISTRATION_PRIVATE_KEY_PEM = `-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCpDZGTI5l43nCe
+/WVe4hLJs4kVuORIGcCismPrktorCihqm2vfJKp4tKTEgD7mjpHODD6WH8IAmEB
xqB8MBD/ZofTl01QCgw7qyYUkoWi+yUIGiDtuT6+H5eg+z2OOypPSBXOMfidxurx
7Z2IOuRxJ2JGFJqPGavCcrLBTd0o2pabo0rJFCiB/1/mQ4n/8HuNUYLnKy0yLeH0
sc1YBeiWJyHm+46Bk6sPGLlXkoNZdexVW+2/C1U2DzpzDzcQc5rKJTm4QS+WZWaY
teNML7SEoBc6xEwx92Q4Uh0gKZ4tqItEwCwz3KXEM4oH+fYWxtAtEXvTaNkJySAj
udyN7DntAgMBAAECggEARjV+ag9047/uIfkea3CckCmTn3/+jv1YCrQ9NdD7PIOT
dGDloOYpuyiar73gbp4E6iMqJC6ww1DJnQUzDaCgzpF0g6noz/78SaOw8wZPPfrz
zEOdvV0b87YHMTJmxDVKQxb7B2G1kUFVvhgjPrrGuT/UDqrr7daJgP5FwwZlfVtu
LebH5/ENtGH0nzeaGpPTtR018r6vBGA3D6s4/iGFnYrxNJXXwFDdhBX6DkuJdwSV
XgGASw53Sk+JbJZ3YOSJDrXWZPyXsPCfj2ywVq0HOKpEZQ0TkuTcHXbP2XmYc6ca
484qxfaOBY6VJMrxE9z240x/wQb7Ak9QIlaO4D2M+QKBgQDLaP+e+vgQuzVY0XA+
V4rp69cnNZLlds0IwvaAfQl1ysM9XWQG8E7hYShDpRHE/E6MuDnYov7nEm3vy2OZ
BPVJq6I7xeIWJGHpK9ePgjUFLSZv2KbgNapDGdd6Ut964tP3p+IpkrfZrB2FZ27J
uQ4Ax3bfBsZyT5L7wWA6CE+M4wKBgQDUwpdtSgJhpybx6GkJwsoS5ER5Jv0lv8mO
YjlUB0zZb8GVbClZkqepEiaT8WRlxvwHdaP4XGm2C/Bp3IMBKqBnLEERS61drXQl
EbUm7jcgOtl8gBrR7E7LR/LiASNecQCSFJvPArkfbRZs4k5u/MTq28y776dZJwfT
N7e8Oqom7wKBgGuET4FoJNErMzKEWfEJ8upcd7hI8CGMHypfa05VSTfS+kooYCPu
x7MH2PGQggj+WEK3ahQha90V97hFaJrMbR8IstMncK7Fgl9uhh1b9MyMpgF+og5n
L10Sfrwwq+HXnbUNL1VMMRPEj0IhfwTvZQByblnKygBIIWgjOcrS88GDAoGBAMWL
T/Inj4KAIsbllfF8LQfRbkpXCyDrrAdJ6BS/GnmhLErCvLnwUz/GHI+syB0/3m5G
qlJF69kdyMFh/zksDPb+vgODEpsyG+73PA3DjOed/KV+hGh5UseoLDnv+JkNrwvz
mp9g1eX58aJzlYOzqlqubq/o2qcKeFeDGlPo3Gd9AoGADuRvbd78duwXlGcAlZSR
45ZjPLwbBydW1kv6s5zYvTSkO7Y8nDrAhFM69WdwowEcY8iVJIoHtpAWoCvdRw8C
+yyeyZNIR7LHseOoD7pI/hZhg6GbFl3KJ7l+RAjZUIawMrzkSvS/VyJufdVTWakI
drSqfOcX13H9kw4eCCVv9Hg=
-----END PRIVATE KEY-----`;

function clone(value) {
    return value == null ? value : JSON.parse(JSON.stringify(value));
}

function createFakeDatabase() {
    const machines = new Map();
    const evhdPasswords = new Map();
    const machineLogSessions = new Map();
    const machineLogEntries = [];
    const runtimeSettings = {
        defaultRetentionActiveDays: 7,
        dailyInspectionHour: 3,
        dailyInspectionMinute: 0,
        timezone: 'UTC',
        lastInspectionAt: null,
    };
    let nextId = 1;
    let nextLogEntryId = 1;

    function nowIso() {
        return new Date().toISOString();
    }

    function sessionKey(machineId, sessionId) {
        return `${machineId}::${sessionId}`;
    }

    function encodeCursor(entry) {
        return Buffer.from(JSON.stringify({
            occurredAt: entry.occurred_at,
            id: entry.id,
        }), 'utf8').toString('base64url');
    }

    function syncPasswordFlag(machineId, record) {
        return {
            ...record,
            evhd_password_configured: evhdPasswords.has(machineId) && Boolean(evhdPasswords.get(machineId)),
        };
    }

    function createRecord(machineId, overrides = {}) {
        const timestamp = nowIso();
        return syncPasswordFlag(machineId, {
            id: nextId++,
            machine_id: machineId,
            protected: false,
            vhd_keyword: 'SDEZ',
            key_id: null,
            key_type: null,
            pubkey_pem: null,
            approved: false,
            approved_at: null,
            revoked: false,
            revoked_at: null,
            last_seen: null,
            registration_cert_fingerprint: null,
            registration_cert_subject: null,
            log_retention_active_days_override: null,
            created_at: timestamp,
            updated_at: timestamp,
            ...overrides,
        });
    }

    function getRecord(machineId) {
        const record = machines.get(machineId);
        return record ? clone(syncPasswordFlag(machineId, record)) : null;
    }

    function saveRecord(machineId, nextRecord) {
        const record = syncPasswordFlag(machineId, {
            ...nextRecord,
            updated_at: nowIso(),
        });
        machines.set(machineId, record);
        return clone(record);
    }

    function ensureMachineExists(machineId) {
        if (!machines.has(machineId)) {
            machines.set(machineId, createRecord(machineId));
        }
    }

    function rebuildSession(machineId, sessionId) {
        const key = sessionKey(machineId, sessionId);
        const relatedEntries = machineLogEntries
            .filter((entry) => entry.machine_id === machineId && entry.session_id === sessionId)
            .sort((left, right) => {
                const timeCompare = new Date(right.occurred_at).getTime() - new Date(left.occurred_at).getTime();
                return timeCompare !== 0 ? timeCompare : right.id - left.id;
            });

        if (relatedEntries.length === 0) {
            machineLogSessions.delete(key);
            return null;
        }

        const oldestEntry = relatedEntries[relatedEntries.length - 1];
        const newestEntry = relatedEntries[0];
        const current = machineLogSessions.get(key) || {
            machine_id: machineId,
            session_id: sessionId,
            app_version: null,
            os_version: null,
            created_at: nowIso(),
        };

        const nextSession = {
            ...current,
            started_at: oldestEntry.occurred_at,
            last_upload_at: nowIso(),
            last_event_at: newestEntry.occurred_at,
            total_count: relatedEntries.length,
            warn_count: relatedEntries.filter((entry) => entry.level === 'warn').length,
            error_count: relatedEntries.filter((entry) => entry.level === 'error').length,
            last_level: newestEntry.level,
            last_component: newestEntry.component,
            updated_at: nowIso(),
        };

        machineLogSessions.set(key, nextSession);
        return clone(nextSession);
    }

    function filterMachineLogEntries(filters = {}) {
        let entries = machineLogEntries.slice();

        if (filters.machineId) {
            entries = entries.filter((entry) => entry.machine_id === filters.machineId);
        }
        if (filters.sessionId) {
            entries = entries.filter((entry) => entry.session_id === filters.sessionId);
        }
        if (filters.level) {
            entries = entries.filter((entry) => entry.level === filters.level);
        }
        if (filters.component) {
            entries = entries.filter((entry) => entry.component === filters.component);
        }
        if (filters.eventKey) {
            entries = entries.filter((entry) => entry.event_key === filters.eventKey);
        }
        if (filters.from) {
            const fromMs = new Date(filters.from).getTime();
            entries = entries.filter((entry) => new Date(entry.occurred_at).getTime() >= fromMs);
        }
        if (filters.to) {
            const toMs = new Date(filters.to).getTime();
            entries = entries.filter((entry) => new Date(entry.occurred_at).getTime() <= toMs);
        }
        if (filters.query) {
            const query = filters.query.toLowerCase();
            entries = entries.filter((entry) => JSON.stringify(entry).toLowerCase().includes(query));
        }

        entries.sort((left, right) => {
            const timeCompare = new Date(right.occurred_at).getTime() - new Date(left.occurred_at).getTime();
            return timeCompare !== 0 ? timeCompare : right.id - left.id;
        });

        if (filters.cursor) {
            const parsed = JSON.parse(Buffer.from(filters.cursor, 'base64url').toString('utf8'));
            entries = entries.filter((entry) => {
                const entryTime = new Date(entry.occurred_at).getTime();
                const cursorTime = new Date(parsed.occurredAt).getTime();
                return entryTime < cursorTime || (entryTime === cursorTime && entry.id < parsed.id);
            });
        }

        return entries;
    }

    return {
        async initialize() {
            return undefined;
        },
        async close() {
            return undefined;
        },
        async getMachine(machineId) {
            return getRecord(machineId);
        },
        async upsertMachine(machineId, isProtected = false, vhdKeyword = 'SDEZ') {
            const current = machines.get(machineId);
            if (!current) {
                const created = createRecord(machineId, {
                    protected: isProtected,
                    vhd_keyword: vhdKeyword,
                });
                machines.set(machineId, created);
                return clone(created);
            }
            return saveRecord(machineId, {
                ...current,
                protected: isProtected,
                vhd_keyword: vhdKeyword,
            });
        },
        async updateMachineProtection(machineId, isProtected) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                protected: isProtected,
            });
        },
        async updateMachineVhdKeyword(machineId, vhdKeyword) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                vhd_keyword: vhdKeyword,
            });
        },
        async getMachineEvhdPassword(machineId) {
            return evhdPasswords.get(machineId) || null;
        },
        async updateMachineEvhdPassword(machineId, evhdPassword) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            evhdPasswords.set(machineId, evhdPassword);
            return saveRecord(machineId, current);
        },
        async getAllMachines() {
            return Array.from(machines.values())
                .sort((left, right) => left.machine_id.localeCompare(right.machine_id))
                .map((record) => clone(syncPasswordFlag(record.machine_id, record)));
        },
        async deleteMachine(machineId) {
            const existed = machines.delete(machineId);
            evhdPasswords.delete(machineId);
            return existed ? { machine_id: machineId } : null;
        },
        async updateMachineLastSeen(machineId) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            const timestamp = nowIso();
            machines.set(machineId, {
                ...current,
                last_seen: timestamp,
                updated_at: timestamp,
            });
            return timestamp;
        },
        async updateMachineKey(machineId, payload) {
            const current = machines.get(machineId) || createRecord(machineId);
            return saveRecord(machineId, {
                ...current,
                key_id: payload.keyId,
                key_type: payload.keyType,
                pubkey_pem: payload.pubkeyPem,
                approved: false,
                approved_at: null,
                revoked: false,
                revoked_at: null,
                registration_cert_fingerprint: payload.registrationCertFingerprint || null,
                registration_cert_subject: payload.registrationCertSubject || null,
            });
        },
        async approveMachine(machineId, approved) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                approved,
                approved_at: approved ? nowIso() : null,
            });
        },
        async revokeMachineKey(machineId) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                key_id: null,
                key_type: null,
                pubkey_pem: null,
                approved: false,
                approved_at: null,
                revoked: true,
                revoked_at: nowIso(),
                registration_cert_fingerprint: null,
                registration_cert_subject: null,
            });
        },
        async updateMachineLogRetentionOverride(machineId, retentionActiveDaysOverride) {
            const current = machines.get(machineId);
            if (!current) {
                return null;
            }
            return saveRecord(machineId, {
                ...current,
                log_retention_active_days_override: retentionActiveDaysOverride,
            });
        },
        async getMachineLogRuntimeSettings() {
            return clone(runtimeSettings);
        },
        async updateMachineLogRuntimeSettings(settings) {
            Object.assign(runtimeSettings, settings || {});
            return clone(runtimeSettings);
        },
        async getMachineLogAcknowledgedSeq(machineId, sessionId) {
            return machineLogEntries
                .filter((entry) => entry.machine_id === machineId && entry.session_id === sessionId)
                .reduce((maxSeq, entry) => Math.max(maxSeq, entry.seq), 0);
        },
        async persistMachineLogBatch({ machineId, sessionId, appVersion, osVersion, entries, uploadRequestId }) {
            ensureMachineExists(machineId);
            let insertedCount = 0;

            for (const entry of entries) {
                const exists = machineLogEntries.some((current) => (
                    current.machine_id === machineId
                    && current.session_id === sessionId
                    && current.seq === entry.seq
                ));
                if (exists) {
                    continue;
                }

                insertedCount += 1;
                machineLogEntries.push({
                    id: nextLogEntryId++,
                    machine_id: machineId,
                    session_id: sessionId,
                    seq: Number(entry.seq),
                    occurred_at: entry.occurredAt,
                    log_day: entry.occurredAt.slice(0, 10),
                    received_at: nowIso(),
                    level: entry.level,
                    component: entry.component,
                    event_key: entry.eventKey,
                    message: entry.message,
                    raw_text: entry.rawText,
                    metadata_json: clone(entry.metadata || {}),
                    upload_request_id: uploadRequestId || null,
                    created_at: nowIso(),
                });
            }

            const key = sessionKey(machineId, sessionId);
            const currentSession = machineLogSessions.get(key) || {
                machine_id: machineId,
                session_id: sessionId,
                app_version: appVersion || null,
                os_version: osVersion || null,
                created_at: nowIso(),
            };
            machineLogSessions.set(key, {
                ...currentSession,
                app_version: appVersion || currentSession.app_version,
                os_version: osVersion || currentSession.os_version,
            });
            rebuildSession(machineId, sessionId);

            return {
                acknowledgedSeq: await this.getMachineLogAcknowledgedSeq(machineId, sessionId),
                insertedCount,
                receivedCount: entries.length,
            };
        },
        async getMachineLogSessions({ machineId, from, to, limit = 50 }) {
            let sessions = Array.from(machineLogSessions.values());
            if (machineId) {
                sessions = sessions.filter((session) => session.machine_id === machineId);
            }
            if (from) {
                const fromMs = new Date(from).getTime();
                sessions = sessions.filter((session) => new Date(session.last_event_at || session.started_at).getTime() >= fromMs);
            }
            if (to) {
                const toMs = new Date(to).getTime();
                sessions = sessions.filter((session) => new Date(session.last_event_at || session.started_at).getTime() <= toMs);
            }

            sessions.sort((left, right) => new Date(right.last_event_at || right.started_at).getTime() - new Date(left.last_event_at || left.started_at).getTime());
            return sessions.slice(0, limit).map((session) => clone(session));
        },
        async getMachineLogs(filters) {
            const limit = Number(filters.limit || 100);
            const entries = filterMachineLogEntries(filters);
            const page = entries.slice(0, limit);
            return {
                entries: page.map((entry) => clone(entry)),
                nextCursor: entries.length > limit ? encodeCursor(page[page.length - 1]) : null,
                hasMore: entries.length > limit,
            };
        },
        async exportMachineLogs(filters) {
            return this.getMachineLogs(filters);
        },
        async runMachineLogRetentionInspection() {
            let deletedEntryCount = 0;
            let deletedSessionCount = 0;

            for (const machine of machines.values()) {
                const retention = machine.log_retention_active_days_override || runtimeSettings.defaultRetentionActiveDays;
                const machineEntries = machineLogEntries
                    .filter((entry) => entry.machine_id === machine.machine_id)
                    .sort((left, right) => right.log_day.localeCompare(left.log_day));
                const activeDays = [...new Set(machineEntries.map((entry) => entry.log_day))];
                const keepDays = new Set(activeDays.slice(0, retention));
                const beforeCount = machineLogEntries.length;
                for (let index = machineLogEntries.length - 1; index >= 0; index--) {
                    const entry = machineLogEntries[index];
                    if (entry.machine_id === machine.machine_id && !keepDays.has(entry.log_day)) {
                        machineLogEntries.splice(index, 1);
                    }
                }
                deletedEntryCount += beforeCount - machineLogEntries.length;
            }

            for (const key of Array.from(machineLogSessions.keys())) {
                const session = machineLogSessions.get(key);
                if (!session) {
                    continue;
                }
                const exists = machineLogEntries.some((entry) => entry.machine_id === session.machine_id && entry.session_id === session.session_id);
                if (!exists) {
                    machineLogSessions.delete(key);
                    deletedSessionCount += 1;
                } else {
                    rebuildSession(session.machine_id, session.session_id);
                }
            }

            runtimeSettings.lastInspectionAt = nowIso();
            return {
                inspectedMachineCount: machines.size,
                deletedEntryCount,
                deletedSessionCount,
                ranAt: runtimeSettings.lastInspectionAt,
            };
        },
    };
}

function buildSignedRegistrationRequest(machineId, keyId, keyType, pubkeyPem, nonce = crypto.randomBytes(16).toString('hex')) {
    const timestamp = Date.now();
    const payload = buildRegistrationSigningPayload({
        machineId,
        keyId,
        keyType,
        pubkeyPem,
        timestamp,
        nonce,
    });
    const signer = crypto.createSign('RSA-SHA256');
    signer.update(payload);
    signer.end();

    return {
        keyId,
        keyType,
        pubkeyPem,
        registrationCertificatePem: TEST_REGISTRATION_CERT_PEM,
        signature: signer.sign(TEST_REGISTRATION_PRIVATE_KEY_PEM, 'base64'),
        timestamp,
        nonce,
    };
}

function deriveMachineLogSessionKeys(sharedSecret, bootstrapSecret, clientNonce, serverNonce) {
    const ikm = Buffer.concat([
        Buffer.from(sharedSecret),
        Buffer.from(String(bootstrapSecret || ''), 'base64'),
    ]);
    const salt = Buffer.from(`${clientNonce}${serverNonce}`, 'utf8');

    return {
        authKey: crypto.hkdfSync('sha256', ikm, salt, Buffer.from('machine-log-ws-auth-v1', 'utf8'), 32),
        sessionKey: crypto.hkdfSync('sha256', ikm, salt, Buffer.from('machine-log-ws-data-v1', 'utf8'), 32),
    };
}

function hashMachineLogTranscript(clientHello, serverHello) {
    return crypto
        .createHash('sha256')
        .update([
            'VHDMounterMachineLogTranscriptV1',
            clientHello.protocolVersion,
            clientHello.machineId,
            clientHello.keyId,
            clientHello.sessionId,
            clientHello.bootstrapId,
            String(clientHello.timestamp),
            clientHello.nonce,
            clientHello.clientEcdhPublicKey,
            serverHello.connectionId,
            String(serverHello.timestamp),
            serverHello.nonce,
            serverHello.serverEcdhPublicKey,
            String(serverHello.heartbeatSeconds),
            String(serverHello.heartbeatTimeoutSeconds),
            String(serverHello.reconnectBaseMs),
            String(serverHello.reconnectMaxMs),
            String(serverHello.resumeWindowSeconds),
            String(serverHello.acknowledgedSeq),
        ].join('\n'), 'utf8')
        .digest();
}

function computeFinishMac(authKey, transcriptHash, label) {
    return crypto
        .createHmac('sha256', authKey)
        .update(transcriptHash)
        .update(label, 'utf8')
        .digest('base64');
}

function waitForWsOpen(ws) {
    return new Promise((resolve, reject) => {
        ws.once('open', resolve);
        ws.once('error', reject);
    });
}

function waitForWsJsonMessage(ws) {
    return new Promise((resolve, reject) => {
        const handleMessage = (raw) => {
            cleanup();
            resolve(JSON.parse(Buffer.isBuffer(raw) ? raw.toString('utf8') : String(raw)));
        };
        const handleError = (error) => {
            cleanup();
            reject(error);
        };
        const handleClose = (code, reason) => {
            cleanup();
            reject(new Error(`WebSocket closed before message: ${code} ${String(reason || '')}`.trim()));
        };
        const cleanup = () => {
            ws.off('message', handleMessage);
            ws.off('error', handleError);
            ws.off('close', handleClose);
        };

        ws.on('message', handleMessage);
        ws.on('error', handleError);
        ws.on('close', handleClose);
    });
}

function waitForWsClose(ws) {
    return new Promise((resolve) => {
        ws.once('close', (code, reason) => {
            resolve({
                code,
                reason: Buffer.isBuffer(reason) ? reason.toString('utf8') : String(reason || ''),
            });
        });
    });
}

async function createInitializedHarness(t) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'vhd-select-server-'));
    t.after(() => {
        fs.rmSync(tempDir, { recursive: true, force: true });
    });

    const fakeDatabase = createFakeDatabase();
    const { app, runtime } = await createApp({
        configDir: tempDir,
        databaseFactory: () => fakeDatabase,
        disableSignalHandlers: true,
    });
    const client = request.agent(app);
    t.after(async () => {
        if (runtime.machineLogInspectionTimer) {
            clearTimeout(runtime.machineLogInspectionTimer);
            runtime.machineLogInspectionTimer = null;
        }
    });

    const prepareResponse = await client
        .post('/api/init/prepare')
        .send({
            issuer: 'VHDMountTest',
            accountName: 'admin',
        })
        .expect(201);

    const totpSecret = prepareResponse.body.totpSecret;
    const completeResponse = await client
        .post('/api/init/complete')
        .send({
            adminPassword: 'ComplexPassword123!',
            sessionSecret: '0123456789abcdef0123456789abcdef0123456789abcdef',
            totpCode: authenticator.generate(totpSecret),
            dbConfig: {
                host: 'localhost',
                port: 5432,
                database: 'vhd_select',
                user: 'tester',
                password: 'secret',
            },
            defaultVhdKeyword: 'SAFEBOOT',
            trustedRegistrationCertificates: [
                {
                    name: 'test-registration-cert',
                    certificatePem: TEST_REGISTRATION_CERT_PEM,
                },
            ],
        })
        .expect(201);

    assert.equal(completeResponse.body.initialized, true);

    return {
        app,
        client,
        fakeDatabase,
        runtime,
        tempDir,
        totpSecret,
    };
}

async function registerApprovedMachine(client, machineId, machineKeyPair, totpSecret) {
    const keyId = `VHDMounterKey_${machineId}`;
    const keyType = 'RSA';
    const pubkeyPem = machineKeyPair.publicKey.export({ type: 'spki', format: 'pem' });
    const signedRequest = buildSignedRegistrationRequest(machineId, keyId, keyType, pubkeyPem);

    await client
        .post(`/api/machines/${machineId}/keys`)
        .send(signedRequest)
        .expect(202);

    await client.post('/api/auth/login').send({ password: 'ComplexPassword123!' }).expect(200);
    await client.post('/api/auth/otp/verify').send({ code: authenticator.generate(totpSecret) }).expect(200);
    await client.post(`/api/machines/${machineId}/approve`).send({ approved: true }).expect(200);

    const bootstrapResponse = await client
        .get('/api/machine-log-bootstrap')
        .query({ machineId })
        .expect(200);

    const bootstrapPlaintext = crypto.privateDecrypt({
        key: machineKeyPair.privateKey,
        padding: crypto.constants.RSA_PKCS1_OAEP_PADDING,
        oaepHash: 'sha1',
    }, Buffer.from(bootstrapResponse.body.logChannelBootstrapCiphertext, 'base64')).toString('utf8');

    return {
        keyId,
        bootstrap: JSON.parse(bootstrapPlaintext),
    };
}

async function createMachineLogTestServer(app, runtime) {
    const server = http.createServer(app);
    attachMachineLogWebSocketServer({
        server,
        runtime,
        logger: {
            log() {},
            warn() {},
            error() {},
        },
    });

    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    return server;
}

async function closeMachineLogTestServer(server) {
    await new Promise((resolve) => server.close(() => resolve()));
}

async function performMachineLogHandshake({ port, machineId, keyId, machineKeyPair, bootstrap, useIncorrectSharedSecret = false }) {
    const ws = new WebSocket(`ws://127.0.0.1:${port}/ws/machine-log`);
    await waitForWsOpen(ws);

    const sessionId = `20260420T010203Z-${useIncorrectSharedSecret ? 'incorrect' : 'raw'}`;
    const clientEcdh = crypto.createECDH('prime256v1');
    clientEcdh.generateKeys();
    const timestamp = Date.now();
    const nonce = crypto.randomBytes(16).toString('hex');
    const clientEcdhPublicKey = clientEcdh.getPublicKey().toString('base64');
    const hello = {
        type: 'client_hello',
        protocolVersion: MACHINE_LOG_PROTOCOL_VERSION,
        machineId,
        keyId,
        sessionId,
        bootstrapId: bootstrap.bootstrapId,
        timestamp,
        nonce,
        clientEcdhPublicKey,
    };
    const helloPayload = buildMachineLogHelloSigningPayload({
        protocolVersion: hello.protocolVersion,
        machineId,
        keyId,
        sessionId,
        bootstrapId: bootstrap.bootstrapId,
        timestamp,
        nonce,
        clientEcdhPublicKey,
    });
    const signer = crypto.createSign('RSA-SHA256');
    signer.update(helloPayload);
    signer.end();

    ws.send(JSON.stringify({
        ...hello,
        signature: signer.sign(machineKeyPair.privateKey, 'base64'),
    }));

    const serverHello = await waitForWsJsonMessage(ws);
    assert.equal(serverHello.type, 'server_hello');

    const rawSharedSecret = clientEcdh.computeSecret(Buffer.from(serverHello.serverEcdhPublicKey, 'base64'));
    const handshakeSharedSecret = useIncorrectSharedSecret
        ? crypto.createHash('sha256').update(rawSharedSecret).digest()
        : rawSharedSecret;
    const derivedKeys = deriveMachineLogSessionKeys(
        handshakeSharedSecret,
        bootstrap.bootstrapSecret,
        nonce,
        serverHello.nonce,
    );
    const transcriptHash = hashMachineLogTranscript(hello, serverHello);

    ws.send(JSON.stringify({
        type: 'client_finish',
        mac: computeFinishMac(derivedKeys.authKey, transcriptHash, 'client_finish'),
    }));

    if (useIncorrectSharedSecret) {
        const closed = await waitForWsClose(ws);
        assert.equal(closed.code, 1008);
        assert.match(closed.reason, /client_finish 校验失败/);
        return;
    }

    const serverFinish = await waitForWsJsonMessage(ws);
    assert.equal(serverFinish.type, 'server_finish');
    assert.equal(serverFinish.mac, computeFinishMac(derivedKeys.authKey, transcriptHash, 'server_finish'));

    await new Promise((resolve) => {
        ws.once('close', () => resolve());
        ws.close();
    });
}

module.exports = {
    TEST_REGISTRATION_CERT_PEM,
    TEST_REGISTRATION_PRIVATE_KEY_PEM,
    attachMachineLogWebSocketServer,
    buildSignedRegistrationRequest,
    clone,
    closeMachineLogTestServer,
    createFakeDatabase,
    createInitializedHarness,
    createMachineLogTestServer,
    deriveMachineLogSessionKeys,
    hashMachineLogTranscript,
    MACHINE_LOG_PROTOCOL_VERSION,
    performMachineLogHandshake,
    registerApprovedMachine,
};
