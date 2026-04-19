const crypto = require('crypto');

const { WebSocketServer } = require('ws');

const { DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS } = require('./database');
const {
    RegistrationAuthError,
    verifySignedMachineLogHello,
} = require('./registrationAuth');
const {
    assertKeyId,
    assertLogComponent,
    assertLogEventKey,
    assertLogLevel,
    assertMachineId,
    assertSessionId,
    normalizeLogEventKey,
} = require('./validators');

const MACHINE_LOG_WS_PATH = '/ws/machine-log';
const MACHINE_LOG_PROTOCOL_VERSION = 'machine-log-ws-v1';
const MACHINE_LOG_BOOTSTRAP_TTL_MS = 15 * 60 * 1000;
const MACHINE_LOG_HEARTBEAT_SECONDS = 15;
const MACHINE_LOG_HEARTBEAT_TIMEOUT_SECONDS = 45;
const MACHINE_LOG_RECONNECT_BASE_MS = 1000;
const MACHINE_LOG_RECONNECT_MAX_MS = 30000;
const MACHINE_LOG_RESUME_WINDOW_SECONDS = 300;
const MACHINE_LOG_MAX_BATCH_SIZE = Number(process.env.MACHINE_LOG_MAX_BATCH_SIZE || 200);
const MACHINE_LOG_MAX_FRAME_BYTES = Number(process.env.MACHINE_LOG_MAX_FRAME_BYTES || 512 * 1024);
const MACHINE_LOG_MAX_BYTES_PER_DAY = Number(process.env.MACHINE_LOG_MAX_BYTES_PER_DAY || 64 * 1024 * 1024);
const MACHINE_LOG_MAX_MACHINE_FRAMES_PER_MINUTE = Number(process.env.MACHINE_LOG_MAX_MACHINE_FRAMES_PER_MINUTE || 120);
const MACHINE_LOG_MAX_IP_FRAMES_PER_MINUTE = Number(process.env.MACHINE_LOG_MAX_IP_FRAMES_PER_MINUTE || 240);

const SENSITIVE_VALUE_PATTERNS = [
    /(password|token|authorization|ciphertext|sessionsecret|registrationcertificatepassword)\s*[:=]\s*([^\s,;]+)/ig,
    /(bearer\s+)([A-Za-z0-9._~-]+)/ig,
];

function generateNonce() {
    return crypto.randomBytes(16).toString('hex');
}

function buildMachineLogTranscript(clientHello, serverHello) {
    return [
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
    ].join('\n');
}

function hashTranscript(clientHello, serverHello) {
    return crypto
        .createHash('sha256')
        .update(buildMachineLogTranscript(clientHello, serverHello), 'utf8')
        .digest();
}

function deriveSessionKeys(sharedSecret, bootstrapSecret, clientNonce, serverNonce) {
    const ikm = Buffer.concat([
        Buffer.from(sharedSecret),
        Buffer.from(String(bootstrapSecret || ''), 'base64'),
    ]);
    const salt = Buffer.from(`${clientNonce}${serverNonce}`, 'utf8');

    const authKey = Buffer.from(
        crypto.hkdfSync(
            'sha256',
            ikm,
            salt,
            Buffer.from('machine-log-ws-auth-v1', 'utf8'),
            32,
        ),
    );
    const sessionKey = Buffer.from(
        crypto.hkdfSync(
            'sha256',
            ikm,
            salt,
            Buffer.from('machine-log-ws-data-v1', 'utf8'),
            32,
        ),
    );

    return {
        authKey,
        sessionKey,
    };
}

function computeFinishMac(authKey, transcriptHash, label) {
    return crypto
        .createHmac('sha256', authKey)
        .update(transcriptHash)
        .update(label, 'utf8')
        .digest('base64');
}

function encryptMachineLogPayload(sessionKey, payload, seq, ack) {
    const iv = crypto.randomBytes(12);
    const cipher = crypto.createCipheriv('aes-256-gcm', sessionKey, iv);
    const plaintext = Buffer.from(JSON.stringify(payload), 'utf8');
    const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
    const tag = cipher.getAuthTag();

    return {
        type: 'encrypted_frame',
        seq,
        ack,
        iv: iv.toString('base64'),
        ciphertext: ciphertext.toString('base64'),
        tag: tag.toString('base64'),
    };
}

function decryptMachineLogPayload(sessionKey, frame) {
    const iv = Buffer.from(String(frame.iv || ''), 'base64');
    const ciphertext = Buffer.from(String(frame.ciphertext || ''), 'base64');
    const tag = Buffer.from(String(frame.tag || ''), 'base64');
    const decipher = crypto.createDecipheriv('aes-256-gcm', sessionKey, iv);
    decipher.setAuthTag(tag);
    const plaintext = Buffer.concat([decipher.update(ciphertext), decipher.final()]);
    return JSON.parse(plaintext.toString('utf8'));
}

function sanitizeMachineLogText(value) {
    let text = String(value || '');
    for (const pattern of SENSITIVE_VALUE_PATTERNS) {
        text = text.replace(pattern, (_match, prefix) => `${prefix}=[REDACTED]`);
    }
    return text.replace(/\u0000/g, '').slice(0, 8192);
}

function sanitizeMachineLogMetadata(value, maxDepth = 4) {
    if (maxDepth <= 0) {
        return '[Truncated]';
    }

    if (value == null) {
        return null;
    }

    if (Array.isArray(value)) {
        return value.slice(0, 64).map((item) => sanitizeMachineLogMetadata(item, maxDepth - 1));
    }

    if (typeof value === 'object') {
        return Object.fromEntries(
            Object.entries(value)
                .slice(0, 64)
                .map(([key, item]) => [String(key).slice(0, 128), sanitizeMachineLogMetadata(item, maxDepth - 1)]),
        );
    }

    if (typeof value === 'string') {
        return sanitizeMachineLogText(value).slice(0, 1024);
    }

    if (typeof value === 'number' || typeof value === 'boolean') {
        return value;
    }

    return sanitizeMachineLogText(String(value)).slice(0, 1024);
}

function normalizeIncomingMachineLogEntry(entry) {
    const sessionId = assertSessionId(entry?.sessionId);
    const seq = Number.parseInt(String(entry?.seq || ''), 10);
    if (!Number.isFinite(seq) || seq <= 0) {
        throw new RegistrationAuthError('seq 必须是正整数');
    }

    const occurredAt = new Date(entry?.occurredAt);
    if (!Number.isFinite(occurredAt.getTime())) {
        throw new RegistrationAuthError('occurredAt 无效');
    }

    const message = sanitizeMachineLogText(entry?.message || entry?.rawText || '');
    const rawText = sanitizeMachineLogText(entry?.rawText || message);

    return {
        sessionId,
        seq,
        occurredAt: occurredAt.toISOString(),
        level: assertLogLevel(entry?.level || 'info'),
        component: assertLogComponent(entry?.component || 'Program'),
        eventKey: assertLogEventKey(normalizeLogEventKey(entry?.eventKey || 'TRACE_LINE')),
        message: message.slice(0, 4096),
        rawText,
        metadata: sanitizeMachineLogMetadata(entry?.metadata || {}),
    };
}

function createMachineLogBootstrap(machineId, publicKeyPem, encryptWithPublicKeyRSA) {
    const bootstrapId = `boot_${crypto.randomUUID().replace(/-/g, '')}`;
    const bootstrapSecret = crypto.randomBytes(32).toString('base64');
    const expiresAt = new Date(Date.now() + MACHINE_LOG_BOOTSTRAP_TTL_MS).toISOString();
    const plaintext = JSON.stringify({
        bootstrapSecret,
        bootstrapId,
        expiresAt,
    });

    return {
        bootstrapId,
        bootstrapSecret,
        bootstrapCiphertext: encryptWithPublicKeyRSA(publicKeyPem, plaintext),
        expiresAt,
        machineId,
    };
}

function cleanupBootstrapCache(runtime) {
    const now = Date.now();
    for (const [bootstrapId, record] of runtime.machineLogBootstrapCache.entries()) {
        if (!record?.expiresAt || Date.parse(record.expiresAt) <= now) {
            runtime.machineLogBootstrapCache.delete(bootstrapId);
        }
    }
}

function consumeSlidingWindow(map, key, maxCount, windowMs) {
    const now = Date.now();
    const current = map.get(key);
    if (!current || now - current.startedAt >= windowMs) {
        map.set(key, { startedAt: now, count: 1 });
        return true;
    }

    if (current.count >= maxCount) {
        return false;
    }

    current.count += 1;
    return true;
}

function consumeDailyBytes(runtime, machineId, bytes) {
    const dayKey = `${machineId}:${new Date().toISOString().slice(0, 10)}`;
    const current = runtime.machineLogDailyBytes.get(dayKey) || 0;
    if (current + bytes > MACHINE_LOG_MAX_BYTES_PER_DAY) {
        return false;
    }
    runtime.machineLogDailyBytes.set(dayKey, current + bytes);
    return true;
}

function buildAuditMetadataFromUpgradeRequest(request) {
    return {
        ip: request.socket?.remoteAddress || '',
        method: 'WS',
        path: request.url || MACHINE_LOG_WS_PATH,
        userAgent: request.headers['user-agent'] || '',
    };
}

function writeMachineLogAudit(runtime, request, entry) {
    if (!runtime?.auditLog) {
        return;
    }

    try {
        runtime.auditLog.append({
            ...buildAuditMetadataFromUpgradeRequest(request),
            ...entry,
        });
    } catch (error) {
        runtime.logger?.error?.('写入机台日志审计失败:', error.message);
    }
}

function buildInspectionClockSnapshot(timeZone) {
    const now = new Date();
    const formatter = new Intl.DateTimeFormat('en-CA', {
        timeZone,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false,
    });

    const parts = formatter.formatToParts(now);
    const year = parts.find((part) => part.type === 'year')?.value;
    const month = parts.find((part) => part.type === 'month')?.value;
    const day = parts.find((part) => part.type === 'day')?.value;
    const hour = Number(parts.find((part) => part.type === 'hour')?.value || '0');
    const minute = Number(parts.find((part) => part.type === 'minute')?.value || '0');

    return {
        dateKey: `${year}-${month}-${day}`,
        minutes: hour * 60 + minute,
    };
}

function shouldRunInspection(settings) {
    const effectiveSettings = {
        ...DEFAULT_MACHINE_LOG_RUNTIME_SETTINGS,
        ...(settings || {}),
    };
    const current = buildInspectionClockSnapshot(effectiveSettings.timezone || 'UTC');
    const targetMinutes = Number(effectiveSettings.dailyInspectionHour || 0) * 60
        + Number(effectiveSettings.dailyInspectionMinute || 0);

    if (current.minutes < targetMinutes) {
        return false;
    }

    if (!effectiveSettings.lastInspectionAt) {
        return true;
    }

    const last = buildInspectionClockSnapshot(effectiveSettings.timezone || 'UTC');
    const lastDate = new Date(effectiveSettings.lastInspectionAt);
    if (!Number.isFinite(lastDate.getTime())) {
        return true;
    }

    const lastFormatter = new Intl.DateTimeFormat('en-CA', {
        timeZone: effectiveSettings.timezone || 'UTC',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
    });
    const lastDateKey = lastFormatter.format(lastDate);
    return lastDateKey !== current.dateKey;
}

async function maybeRunMachineLogInspection(runtime, logger) {
    if (runtime.machineLogInspectionRunning || !runtime.database) {
        return;
    }

    const settings = await runtime.database.getMachineLogRuntimeSettings();
    if (!shouldRunInspection(settings)) {
        return;
    }

    runtime.machineLogInspectionRunning = true;
    try {
        const result = await runtime.database.runMachineLogRetentionInspection('system');
        logger.log('机台日志保留巡检完成:', result);
    } catch (error) {
        logger.error('机台日志保留巡检失败:', error.message);
    } finally {
        runtime.machineLogInspectionRunning = false;
    }
}

function startMachineLogInspectionScheduler(runtime, logger = console) {
    if (runtime.machineLogInspectionTimer) {
        return runtime.machineLogInspectionTimer;
    }

    const timer = setInterval(() => {
        maybeRunMachineLogInspection(runtime, logger).catch((error) => {
            logger.error('机台日志保留巡检调度失败:', error.message);
        });
    }, 60 * 1000);

    if (typeof timer.unref === 'function') {
        timer.unref();
    }

    runtime.machineLogInspectionTimer = timer;
    maybeRunMachineLogInspection(runtime, logger).catch((error) => {
        logger.error('机台日志首次保留巡检失败:', error.message);
    });
    return timer;
}

function stopMachineLogInspectionScheduler(runtime) {
    if (!runtime.machineLogInspectionTimer) {
        return;
    }

    clearInterval(runtime.machineLogInspectionTimer);
    runtime.machineLogInspectionTimer = null;
}

function buildMachineLogTextExport(entries) {
    return entries
        .map((entry) => {
            const occurredAt = entry.occurred_at || entry.occurredAt || '';
            const level = String(entry.level || '').toUpperCase();
            const component = entry.component || 'Program';
            const eventKey = entry.event_key || entry.eventKey || 'TRACE_LINE';
            const message = entry.raw_text || entry.rawText || entry.message || '';
            return `[${occurredAt}] [${level}] [${component}/${eventKey}] ${message}`;
        })
        .join('\n');
}

function attachMachineLogWebSocketServer({ server, runtime, logger = console }) {
    const wss = new WebSocketServer({ noServer: true });
    runtime.machineLogWebSocketServer = wss;

    function closeWithPolicyViolation(ws, reason = 'policy') {
        try {
            ws.close(1008, reason.slice(0, 120));
        } catch {
            ws.terminate();
        }
    }

    function createConnectionState(ws, request) {
        return {
            ws,
            request,
            pendingHandshake: null,
            handshakeComplete: false,
            closed: false,
            outboundFrameSeq: 0,
            acknowledgedSeq: 0,
            uploadedCount: 0,
            machineId: null,
            sessionId: null,
            connectionId: null,
            authKey: null,
            sessionKey: null,
            heartbeatTimeoutMs: MACHINE_LOG_HEARTBEAT_TIMEOUT_SECONDS * 1000,
            timeoutHandle: null,
        };
    }

    function resetConnectionTimeout(state) {
        if (state.timeoutHandle) {
            clearTimeout(state.timeoutHandle);
        }

        state.timeoutHandle = setTimeout(() => {
            closeWithPolicyViolation(state.ws, 'timeout');
        }, state.heartbeatTimeoutMs);

        if (typeof state.timeoutHandle.unref === 'function') {
            state.timeoutHandle.unref();
        }
    }

    function clearConnectionState(state) {
        if (state.closed) {
            return;
        }
        state.closed = true;

        if (state.timeoutHandle) {
            clearTimeout(state.timeoutHandle);
            state.timeoutHandle = null;
        }

        const current = runtime.machineLogConnections.get(state.machineId || '');
        if (current?.ws === state.ws) {
            runtime.machineLogConnections.delete(state.machineId);
        }

        if (state.uploadedCount > 0) {
            writeMachineLogAudit(runtime, state.request, {
                type: 'machine.log.upload',
                actor: 'machine',
                result: 'success',
                machineId: state.machineId,
                sessionId: state.sessionId,
                uploadedCount: state.uploadedCount,
                connectionId: state.connectionId,
            });
        }
    }

    async function sendEncryptedPayload(state, payload) {
        state.outboundFrameSeq += 1;
        const frame = encryptMachineLogPayload(
            state.sessionKey,
            payload,
            state.outboundFrameSeq,
            state.acknowledgedSeq,
        );
        state.ws.send(JSON.stringify(frame));
    }

    async function handleEncryptedPayload(state, payload, rawSize) {
        const machineWindowKey = `machine:${state.machineId}`;
        const ipWindowKey = `ip:${state.request.socket?.remoteAddress || ''}`;
        if (!consumeSlidingWindow(runtime.machineLogUploadWindows, machineWindowKey, MACHINE_LOG_MAX_MACHINE_FRAMES_PER_MINUTE, 60 * 1000)) {
            throw new RegistrationAuthError('机台上传过于频繁');
        }
        if (!consumeSlidingWindow(runtime.machineLogUploadWindows, ipWindowKey, MACHINE_LOG_MAX_IP_FRAMES_PER_MINUTE, 60 * 1000)) {
            throw new RegistrationAuthError('来源地址上传过于频繁');
        }
        if (!consumeDailyBytes(runtime, state.machineId, rawSize)) {
            throw new RegistrationAuthError('机台当日日志体积超过上限');
        }

        switch (payload.type) {
            case 'log_batch': {
                const entries = Array.isArray(payload.entries) ? payload.entries : [];
                if (entries.length === 0) {
                    await sendEncryptedPayload(state, {
                        type: 'ack',
                        sessionId: state.sessionId,
                        acknowledgedSeq: state.acknowledgedSeq,
                        insertedCount: 0,
                    });
                    return;
                }
                if (entries.length > MACHINE_LOG_MAX_BATCH_SIZE) {
                    throw new RegistrationAuthError(`单批日志数量不能超过 ${MACHINE_LOG_MAX_BATCH_SIZE}`);
                }

                const normalizedEntries = entries.map((entry) => normalizeIncomingMachineLogEntry(entry));
                if (normalizedEntries.some((entry) => entry.sessionId !== state.sessionId)) {
                    throw new RegistrationAuthError('日志批次中的 sessionId 与握手会话不一致');
                }

                const result = await runtime.database.persistMachineLogBatch({
                    machineId: state.machineId,
                    sessionId: state.sessionId,
                    appVersion: payload.appVersion || null,
                    osVersion: payload.osVersion || null,
                    entries: normalizedEntries,
                    uploadRequestId: state.connectionId,
                });

                state.acknowledgedSeq = Math.max(state.acknowledgedSeq, result.acknowledgedSeq);
                state.uploadedCount += result.insertedCount;

                await sendEncryptedPayload(state, {
                    type: 'ack',
                    sessionId: state.sessionId,
                    acknowledgedSeq: state.acknowledgedSeq,
                    insertedCount: result.insertedCount,
                    receivedCount: result.receivedCount,
                });
                return;
            }
            case 'heartbeat': {
                await sendEncryptedPayload(state, {
                    type: 'heartbeat',
                    sessionId: state.sessionId,
                    acknowledgedSeq: state.acknowledgedSeq,
                    serverTime: new Date().toISOString(),
                });
                return;
            }
            case 'resume': {
                state.acknowledgedSeq = await runtime.database.getMachineLogAcknowledgedSeq(state.machineId, state.sessionId);
                await sendEncryptedPayload(state, {
                    type: 'ack',
                    sessionId: state.sessionId,
                    acknowledgedSeq: state.acknowledgedSeq,
                    insertedCount: 0,
                });
                return;
            }
            case 'rekey': {
                await sendEncryptedPayload(state, {
                    type: 'close',
                    reason: 'rekey-required',
                });
                closeWithPolicyViolation(state.ws, 'rekey-required');
                return;
            }
            case 'close': {
                try {
                    state.ws.close(1000, 'client-close');
                } catch {
                    state.ws.terminate();
                }
                return;
            }
            default:
                throw new RegistrationAuthError('不支持的加密业务帧类型');
        }
    }

    async function handleHandshakeMessage(state, message) {
        if (message.type === 'client_hello') {
            cleanupBootstrapCache(runtime);

            if (!runtime.initialized || !runtime.database) {
                throw new RegistrationAuthError('服务尚未准备好');
            }

            if (message.protocolVersion !== MACHINE_LOG_PROTOCOL_VERSION) {
                throw new RegistrationAuthError('机台日志协议版本不兼容');
            }

            const machineId = assertMachineId(message.machineId);
            const keyId = assertKeyId(message.keyId);
            const sessionId = assertSessionId(message.sessionId);
            const bootstrapId = String(message.bootstrapId || '').trim();
            const clientEcdhPublicKey = String(message.clientEcdhPublicKey || '').trim();
            if (!bootstrapId) {
                throw new RegistrationAuthError('缺少 bootstrapId');
            }
            if (!clientEcdhPublicKey) {
                throw new RegistrationAuthError('缺少 clientEcdhPublicKey');
            }

            const machine = await runtime.database.getMachine(machineId);
            if (!machine) {
                throw new RegistrationAuthError('机台不存在', 404);
            }
            if (machine.revoked) {
                throw new RegistrationAuthError('机台密钥已吊销');
            }
            if (!machine.approved) {
                throw new RegistrationAuthError('机台密钥未审批');
            }
            if (!machine.pubkey_pem) {
                throw new RegistrationAuthError('机台未注册公钥');
            }

            const bootstrapRecord = runtime.machineLogBootstrapCache.get(bootstrapId);
            if (!bootstrapRecord || bootstrapRecord.machineId !== machineId) {
                throw new RegistrationAuthError('bootstrapId 无效或已过期');
            }
            if (Date.parse(bootstrapRecord.expiresAt) <= Date.now()) {
                runtime.machineLogBootstrapCache.delete(bootstrapId);
                throw new RegistrationAuthError('bootstrapId 已过期');
            }

            verifySignedMachineLogHello({
                protocolVersion: message.protocolVersion,
                machineId,
                keyId,
                sessionId,
                bootstrapId,
                publicKeyPem: machine.pubkey_pem,
                signature: String(message.signature || '').trim(),
                timestamp: message.timestamp,
                nonce: String(message.nonce || '').trim(),
                clientEcdhPublicKey,
                nonceCache: runtime.machineLogRequestNonceCache,
            });

            let sharedSecret;
            const serverEcdh = crypto.createECDH('prime256v1');
            serverEcdh.generateKeys();
            try {
                sharedSecret = serverEcdh.computeSecret(Buffer.from(clientEcdhPublicKey, 'base64'));
            } catch {
                throw new RegistrationAuthError('clientEcdhPublicKey 无效');
            }

            const connectionId = `conn_${crypto.randomUUID().replace(/-/g, '')}`;
            const acknowledgedSeq = await runtime.database.getMachineLogAcknowledgedSeq(machineId, sessionId);
            const serverHello = {
                type: 'server_hello',
                protocolVersion: MACHINE_LOG_PROTOCOL_VERSION,
                connectionId,
                bootstrapId,
                timestamp: Date.now(),
                nonce: generateNonce(),
                serverEcdhPublicKey: serverEcdh.getPublicKey().toString('base64'),
                heartbeatSeconds: MACHINE_LOG_HEARTBEAT_SECONDS,
                heartbeatTimeoutSeconds: MACHINE_LOG_HEARTBEAT_TIMEOUT_SECONDS,
                reconnectBaseMs: MACHINE_LOG_RECONNECT_BASE_MS,
                reconnectMaxMs: MACHINE_LOG_RECONNECT_MAX_MS,
                resumeWindowSeconds: MACHINE_LOG_RESUME_WINDOW_SECONDS,
                acknowledgedSeq,
            };
            const { authKey, sessionKey } = deriveSessionKeys(
                sharedSecret,
                bootstrapRecord.bootstrapSecret,
                String(message.nonce || '').trim(),
                serverHello.nonce,
            );

            state.pendingHandshake = {
                clientHello: {
                    protocolVersion: message.protocolVersion,
                    machineId,
                    keyId,
                    sessionId,
                    bootstrapId,
                    timestamp: Number(message.timestamp),
                    nonce: String(message.nonce || '').trim(),
                    clientEcdhPublicKey,
                },
                serverHello,
                transcriptHash: hashTranscript({
                    protocolVersion: message.protocolVersion,
                    machineId,
                    keyId,
                    sessionId,
                    bootstrapId,
                    timestamp: Number(message.timestamp),
                    nonce: String(message.nonce || '').trim(),
                    clientEcdhPublicKey,
                }, serverHello),
                authKey,
                sessionKey,
                machineId,
                sessionId,
                connectionId,
            };

            state.machineId = machineId;
            state.sessionId = sessionId;
            state.connectionId = connectionId;
            state.acknowledgedSeq = acknowledgedSeq;
            state.heartbeatTimeoutMs = serverHello.heartbeatTimeoutSeconds * 1000;
            state.ws.send(JSON.stringify(serverHello));
            return;
        }

        if (message.type === 'client_finish') {
            if (!state.pendingHandshake) {
                throw new RegistrationAuthError('握手状态无效');
            }

            const expectedMac = computeFinishMac(
                state.pendingHandshake.authKey,
                state.pendingHandshake.transcriptHash,
                'client_finish',
            );
            if (String(message.mac || '').trim() !== expectedMac) {
                throw new RegistrationAuthError('client_finish 校验失败');
            }

            const existingConnection = runtime.machineLogConnections.get(state.machineId);
            if (existingConnection && existingConnection.ws !== state.ws) {
                try {
                    existingConnection.ws.close(1012, 'superseded');
                } catch {
                    existingConnection.ws.terminate();
                }
            }

            state.handshakeComplete = true;
            state.authKey = state.pendingHandshake.authKey;
            state.sessionKey = state.pendingHandshake.sessionKey;
            runtime.machineLogConnections.set(state.machineId, {
                ws: state.ws,
                machineId: state.machineId,
                sessionId: state.sessionId,
                connectionId: state.connectionId,
            });

            state.ws.send(JSON.stringify({
                type: 'server_finish',
                mac: computeFinishMac(state.authKey, state.pendingHandshake.transcriptHash, 'server_finish'),
            }));
            state.pendingHandshake = null;
            return;
        }

        throw new RegistrationAuthError('不支持的握手消息类型');
    }

    wss.on('connection', (ws, request) => {
        const state = createConnectionState(ws, request);
        resetConnectionTimeout(state);

        ws.on('message', async (raw) => {
            resetConnectionTimeout(state);

            try {
                const rawText = Buffer.isBuffer(raw) ? raw.toString('utf8') : String(raw);
                if (Buffer.byteLength(rawText, 'utf8') > MACHINE_LOG_MAX_FRAME_BYTES) {
                    throw new RegistrationAuthError('帧体积超过上限');
                }

                const message = JSON.parse(rawText);

                if (!state.handshakeComplete) {
                    await handleHandshakeMessage(state, message);
                    return;
                }

                if (message.type !== 'encrypted_frame') {
                    throw new RegistrationAuthError('握手完成后仅允许加密帧');
                }

                const payload = decryptMachineLogPayload(state.sessionKey, message);
                await handleEncryptedPayload(state, payload, Buffer.byteLength(rawText, 'utf8'));
            } catch (error) {
                logger.error('机台日志 WebSocket 处理失败:', error.message || error);
                writeMachineLogAudit(runtime, request, {
                    type: 'machine.log.upload',
                    actor: 'machine',
                    result: 'failure',
                    machineId: state.machineId,
                    sessionId: state.sessionId,
                    reason: error.message || 'unknown-error',
                });
                closeWithPolicyViolation(ws, error.message || 'machine-log-error');
            }
        });

        ws.on('close', () => {
            clearConnectionState(state);
        });

        ws.on('error', () => {
            clearConnectionState(state);
        });
    });

    server.on('upgrade', (request, socket, head) => {
        try {
            const url = new URL(request.url || '', 'http://localhost');
            if (url.pathname !== MACHINE_LOG_WS_PATH) {
                return;
            }

            wss.handleUpgrade(request, socket, head, (ws) => {
                wss.emit('connection', ws, request);
            });
        } catch {
            socket.destroy();
        }
    });

    return wss;
}

module.exports = {
    attachMachineLogWebSocketServer,
    buildMachineLogTextExport,
    createMachineLogBootstrap,
    MACHINE_LOG_PROTOCOL_VERSION,
    MACHINE_LOG_WS_PATH,
    sanitizeMachineLogText,
    startMachineLogInspectionScheduler,
    stopMachineLogInspectionScheduler,
};