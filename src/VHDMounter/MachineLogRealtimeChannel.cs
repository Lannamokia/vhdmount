using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VHDMounter
{
    internal sealed class MachineLogRealtimeChannel : IDisposable
    {
        private const string MachineLogProtocolVersion = "machine-log-ws-v1";
        private static readonly TimeSpan MinimumBootstrapRefreshWindow = TimeSpan.FromSeconds(30);
        private static readonly HttpClient SharedHttpClient = new HttpClient();

        private readonly MachineLogClientConfiguration configuration;
        private readonly MachineLogBuffer buffer;
        private readonly Action<string> diagnostics;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        private readonly string machineId;
        private readonly string keyId;
        private readonly string appVersion;
        private readonly string osVersion;
        private readonly string timeZone;

        private Task backgroundTask;
        private CancellationTokenSource lifetimeCts;
        private bool disposed;
        private MachineLogBootstrapToken cachedBootstrap;
        private DateTimeOffset? nextRegistrationAttemptUtc;
        private string pendingRegistrationBackoffMessage;

        public MachineLogRealtimeChannel(
            MachineLogClientConfiguration configuration,
            MachineLogBuffer buffer,
            Action<string> diagnostics = null)
        {
            this.configuration = configuration;
            this.buffer = buffer;
            this.diagnostics = diagnostics;
            machineId = configuration.MachineId;
            keyId = $"VHDMounterKey_{machineId}";
            appVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            osVersion = Environment.OSVersion.VersionString;
            timeZone = TimeZoneInfo.Local.Id;
        }

        public void Start(CancellationToken cancellationToken)
        {
            if (disposed || backgroundTask != null || !configuration.EnableLogUpload)
            {
                return;
            }

            lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            backgroundTask = Task.Run(() => RunAsync(lifetimeCts.Token), lifetimeCts.Token);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetimeCts?.Cancel();
            try
            {
                backgroundTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
            finally
            {
                lifetimeCts?.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var reconnectBaseMs = 1000;
            var reconnectMaxMs = 30000;
            var consecutiveFailures = 0;

            diagnostics?.Invoke($"机台日志实时通道启动，MachineId={machineId} SessionId={buffer.CurrentSessionId}");

            while (!cancellationToken.IsCancellationRequested)
            {
                var targetSessionId = GetNextSessionId();
                try
                {
                    var bootstrap = await EnsureBootstrapAsync(cancellationToken).ConfigureAwait(false);
                    if (bootstrap == null)
                    {
                        consecutiveFailures += 1;
                        await DelayForReconnectAsync(reconnectBaseMs, reconnectMaxMs, consecutiveFailures, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var connectionResult = await RunConnectionAsync(targetSessionId, bootstrap, cancellationToken).ConfigureAwait(false);
                    reconnectBaseMs = connectionResult.ReconnectBaseMs;
                    reconnectMaxMs = connectionResult.ReconnectMaxMs;
                    consecutiveFailures = 0;

                    if (connectionResult.SwitchSessionImmediately)
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    diagnostics?.Invoke($"机台日志通道异常: {MachineLogSanitizer.SanitizeSensitiveText(ex.Message)}");
                    consecutiveFailures += 1;
                }

                await DelayForReconnectAsync(reconnectBaseMs, reconnectMaxMs, consecutiveFailures, cancellationToken).ConfigureAwait(false);
            }

            diagnostics?.Invoke("机台日志实时通道已停止");
        }

        private string GetNextSessionId()
        {
            var pendingSessionIds = buffer.GetPendingSessionIds();
            return pendingSessionIds.Count > 0 ? pendingSessionIds[0] : buffer.CurrentSessionId;
        }

        private async Task<ConnectionRunResult> RunConnectionAsync(
            string sessionId,
            MachineLogBootstrapToken bootstrap,
            CancellationToken cancellationToken)
        {
            using var clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
            await clientWebSocket.ConnectAsync(configuration.ResolveWebSocketUri(), cancellationToken).ConfigureAwait(false);

            var handshake = await PerformHandshakeAsync(clientWebSocket, sessionId, bootstrap, cancellationToken).ConfigureAwait(false);
            buffer.Acknowledge(sessionId, handshake.ServerHello.AcknowledgedSeq);

            if (sessionId != buffer.CurrentSessionId && !buffer.HasPendingEntries(sessionId, handshake.ServerHello.AcknowledgedSeq))
            {
                await CloseConnectionSilentlyAsync(clientWebSocket, cancellationToken).ConfigureAwait(false);
                return new ConnectionRunResult(handshake.ServerHello.ReconnectBaseMs, handshake.ServerHello.ReconnectMaxMs, true);
            }

            var serverSignal = new SemaphoreSlim(0, int.MaxValue);
            var sendLock = new SemaphoreSlim(1, 1);
            var receiverError = default(Exception);
            var remoteCloseReason = string.Empty;
            var lastAcknowledgedSeq = handshake.ServerHello.AcknowledgedSeq;
            var outboundFrameSeq = 0L;
            var lastServerActivityUtc = DateTimeOffset.UtcNow;
            var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(5, handshake.ServerHello.HeartbeatSeconds));
            var heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(handshake.ServerHello.HeartbeatTimeoutSeconds, handshake.ServerHello.HeartbeatSeconds + 10));
            var nextHeartbeatUtc = DateTimeOffset.UtcNow.Add(heartbeatInterval);
            var nextBatchUtc = DateTimeOffset.UtcNow;

            async Task SendEncryptedPayloadAsync(object payload)
            {
                await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    outboundFrameSeq += 1;
                    var frameJson = SerializeEncryptedFrameJson(
                        handshake.SessionKey,
                        payload,
                        outboundFrameSeq,
                        lastAcknowledgedSeq);
                    await SendTextMessageAsync(clientWebSocket, frameJson, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    sendLock.Release();
                }
            }

            void ApplyAcknowledgement(long acknowledgedSeq)
            {
                if (acknowledgedSeq <= lastAcknowledgedSeq)
                {
                    return;
                }

                lastAcknowledgedSeq = acknowledgedSeq;
                buffer.Acknowledge(sessionId, acknowledgedSeq);
            }

            async Task ReceiveLoopAsync()
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested && clientWebSocket.State == WebSocketState.Open)
                    {
                        var rawText = await ReceiveTextMessageAsync(clientWebSocket, cancellationToken).ConfigureAwait(false);
                        lastServerActivityUtc = DateTimeOffset.UtcNow;

                        using var frameDoc = JsonDocument.Parse(rawText);
                        var frameRoot = frameDoc.RootElement;
                        if (!TryGetString(frameRoot, "type", out var frameType) ||
                            !string.Equals(frameType, "encrypted_frame", StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("服务端返回了无法识别的帧类型");
                        }

                        using var payloadDoc = JsonDocument.Parse(DecryptFramePayload(handshake.SessionKey, frameRoot));
                        var payloadRoot = payloadDoc.RootElement;
                        if (!TryGetString(payloadRoot, "type", out var payloadType))
                        {
                            throw new InvalidOperationException("服务端返回了缺少 type 的业务帧");
                        }

                        switch (payloadType)
                        {
                            case "ack":
                                ApplyAcknowledgement(GetInt64(payloadRoot, "acknowledgedSeq"));
                                ReleaseSignal(serverSignal);
                                break;
                            case "heartbeat":
                                ApplyAcknowledgement(GetInt64(payloadRoot, "acknowledgedSeq"));
                                ReleaseSignal(serverSignal);
                                break;
                            case "close":
                                remoteCloseReason = TryGetString(payloadRoot, "reason", out var reason)
                                    ? reason
                                    : "server-close";
                                ReleaseSignal(serverSignal);
                                return;
                            default:
                                throw new InvalidOperationException($"服务端返回了不支持的业务帧类型: {payloadType}");
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    receiverError = ex;
                    ReleaseSignal(serverSignal);
                }
            }

            var receiverTask = Task.Run(ReceiveLoopAsync, cancellationToken);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (receiverError != null)
                    {
                        throw receiverError;
                    }

                    if (!string.IsNullOrWhiteSpace(remoteCloseReason))
                    {
                        throw new InvalidOperationException($"服务端主动关闭机台日志通道: {remoteCloseReason}");
                    }

                    if (DateTimeOffset.UtcNow - lastServerActivityUtc > heartbeatTimeout)
                    {
                        throw new TimeoutException("服务端心跳超时");
                    }

                    if (sessionId != buffer.CurrentSessionId && !buffer.HasPendingEntries(sessionId, lastAcknowledgedSeq))
                    {
                        await CloseConnectionSilentlyAsync(clientWebSocket, cancellationToken).ConfigureAwait(false);
                        return new ConnectionRunResult(handshake.ServerHello.ReconnectBaseMs, handshake.ServerHello.ReconnectMaxMs, true);
                    }

                    var batch = buffer.GetPendingBatch(sessionId, lastAcknowledgedSeq, configuration.MachineLogUploadBatchSize);
                    if (batch.Count > 0 && DateTimeOffset.UtcNow >= nextBatchUtc)
                    {
                        var batchTargetSeq = batch[batch.Count - 1].Seq;
                        await SendEncryptedPayloadAsync(new
                        {
                            type = "log_batch",
                            sessionId,
                            appVersion,
                            osVersion,
                            timezone = timeZone,
                            entries = batch,
                        }).ConfigureAwait(false);
                        await WaitForAcknowledgementAsync(
                            batchTargetSeq,
                            () => lastAcknowledgedSeq,
                            () => remoteCloseReason,
                            serverSignal,
                            heartbeatTimeout,
                            cancellationToken).ConfigureAwait(false);

                        nextBatchUtc = DateTimeOffset.UtcNow.AddMilliseconds(configuration.MachineLogUploadIntervalMs);
                        nextHeartbeatUtc = DateTimeOffset.UtcNow.Add(heartbeatInterval);
                        continue;
                    }

                    if (DateTimeOffset.UtcNow >= nextHeartbeatUtc)
                    {
                        await SendEncryptedPayloadAsync(new
                        {
                            type = "heartbeat",
                            sessionId,
                        }).ConfigureAwait(false);
                        nextHeartbeatUtc = DateTimeOffset.UtcNow.Add(heartbeatInterval);
                    }

                    await buffer.WaitForNewEntriesAsync(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await CloseConnectionSilentlyAsync(clientWebSocket, cancellationToken).ConfigureAwait(false);
                ReleaseSignal(serverSignal);
                try
                {
                    await receiverTask.ConfigureAwait(false);
                }
                catch
                {
                }
                serverSignal.Dispose();
                sendLock.Dispose();
            }

            return new ConnectionRunResult(handshake.ServerHello.ReconnectBaseMs, handshake.ServerHello.ReconnectMaxMs, false);
        }

        private async Task<HandshakeState> PerformHandshakeAsync(
            ClientWebSocket clientWebSocket,
            string sessionId,
            MachineLogBootstrapToken bootstrap,
            CancellationToken cancellationToken)
        {
            using var machineRsa = VHDManager.EnsureOrCreateTpmRsa(machineId);
            using var clientEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var clientNonce = GenerateNonce();
            var clientEcdhPublicKey = Convert.ToBase64String(ExportEcPoint(clientEcdh.ExportParameters(false)));
            var signingPayload = BuildMachineLogHelloSigningPayload(
                MachineLogProtocolVersion,
                machineId,
                keyId,
                sessionId,
                bootstrap.BootstrapId,
                timestamp,
                clientNonce,
                clientEcdhPublicKey);
            var signature = Convert.ToBase64String(machineRsa.SignData(
                Encoding.UTF8.GetBytes(signingPayload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

            await SendTextMessageAsync(clientWebSocket, JsonSerializer.Serialize(new
            {
                type = "client_hello",
                protocolVersion = MachineLogProtocolVersion,
                machineId,
                keyId,
                sessionId,
                bootstrapId = bootstrap.BootstrapId,
                timestamp,
                nonce = clientNonce,
                clientEcdhPublicKey,
                signature,
            }, jsonOptions), cancellationToken).ConfigureAwait(false);

            using var serverHelloDoc = JsonDocument.Parse(await ReceiveTextMessageAsync(clientWebSocket, cancellationToken).ConfigureAwait(false));
            var serverHello = ParseServerHello(serverHelloDoc.RootElement, bootstrap.BootstrapId);
            var sharedSecret = clientEcdh.DeriveKeyMaterial(ImportRemotePublicKey(serverHello.ServerEcdhPublicKey));
            var derivedKeys = DeriveSessionKeys(sharedSecret, bootstrap.BootstrapSecret, clientNonce, serverHello.Nonce);
            var transcriptHash = HashTranscript(
                MachineLogProtocolVersion,
                machineId,
                keyId,
                sessionId,
                bootstrap.BootstrapId,
                timestamp,
                clientNonce,
                clientEcdhPublicKey,
                serverHello);

            await SendTextMessageAsync(clientWebSocket, JsonSerializer.Serialize(new
            {
                type = "client_finish",
                mac = ComputeFinishMac(derivedKeys.AuthKey, transcriptHash, "client_finish"),
            }, jsonOptions), cancellationToken).ConfigureAwait(false);

            using var serverFinishDoc = JsonDocument.Parse(await ReceiveTextMessageAsync(clientWebSocket, cancellationToken).ConfigureAwait(false));
            var serverFinishRoot = serverFinishDoc.RootElement;
            if (!TryGetString(serverFinishRoot, "type", out var finishType) ||
                !string.Equals(finishType, "server_finish", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("服务端未返回 server_finish");
            }

            var expectedServerMac = ComputeFinishMac(derivedKeys.AuthKey, transcriptHash, "server_finish");
            if (!TryGetString(serverFinishRoot, "mac", out var serverMac) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedServerMac),
                    Encoding.UTF8.GetBytes(serverMac)))
            {
                throw new InvalidOperationException("server_finish 校验失败");
            }

            diagnostics?.Invoke($"机台日志握手完成: SessionId={sessionId} Ack={serverHello.AcknowledgedSeq}");
            return new HandshakeState(serverHello, derivedKeys.AuthKey, derivedKeys.SessionKey);
        }

        private async Task<MachineLogBootstrapToken> EnsureBootstrapAsync(CancellationToken cancellationToken)
        {
            if (cachedBootstrap != null && cachedBootstrap.ExpiresAt > DateTimeOffset.UtcNow.Add(MinimumBootstrapRefreshWindow))
            {
                return cachedBootstrap;
            }

            var envelopeUrl = configuration.ResolveEnvelopeUrl();
            var envelopeRequestUri = new UriBuilder(envelopeUrl)
            {
                Query = $"machineId={Uri.EscapeDataString(machineId)}",
            }.Uri;

            using var response = await SharedHttpClient.GetAsync(envelopeRequestUri, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var parsedError = ParseResponseError(responseBody);
                if ((int)response.StatusCode == 400 && parsedError.Contains("未注册公钥", StringComparison.OrdinalIgnoreCase))
                {
                    if (await TryRegisterPublicKeyAsync(envelopeUrl, cancellationToken).ConfigureAwait(false))
                    {
                        return await FetchBootstrapAfterRegistrationAsync(envelopeRequestUri, cancellationToken).ConfigureAwait(false);
                    }
                }

                diagnostics?.Invoke($"获取机台日志 bootstrap 失败: {(int)response.StatusCode} {parsedError}");
                return null;
            }

            cachedBootstrap = ParseBootstrapToken(responseBody);
            return cachedBootstrap;
        }

        private async Task<MachineLogBootstrapToken> FetchBootstrapAfterRegistrationAsync(Uri envelopeRequestUri, CancellationToken cancellationToken)
        {
            using var retryResponse = await SharedHttpClient.GetAsync(envelopeRequestUri, cancellationToken).ConfigureAwait(false);
            var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!retryResponse.IsSuccessStatusCode)
            {
                diagnostics?.Invoke($"注册公钥后获取机台日志 bootstrap 仍失败: {(int)retryResponse.StatusCode} {ParseResponseError(retryBody)}");
                return null;
            }

            cachedBootstrap = ParseBootstrapToken(retryBody);
            return cachedBootstrap;
        }

        private MachineLogBootstrapToken ParseBootstrapToken(string responseBody)
        {
            using var responseDoc = JsonDocument.Parse(responseBody);
            var root = responseDoc.RootElement;
            var bootstrapId = GetRequiredString(root, "logChannelBootstrapId");
            var bootstrapCiphertext = GetRequiredString(root, "logChannelBootstrapCiphertext");
            var bootstrapExpiresAt = GetRequiredString(root, "logChannelBootstrapExpiresAt");

            using var machineRsa = VHDManager.EnsureOrCreateTpmRsa(machineId);
            var plaintextBytes = machineRsa.Decrypt(
                Convert.FromBase64String(bootstrapCiphertext),
                RSAEncryptionPadding.OaepSHA1);
            using var plaintextDoc = JsonDocument.Parse(plaintextBytes);
            var payload = plaintextDoc.RootElement;
            var decryptedBootstrapId = GetRequiredString(payload, "bootstrapId");
            if (!string.Equals(bootstrapId, decryptedBootstrapId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("bootstrapId 与解密内容不一致");
            }

            return new MachineLogBootstrapToken(
                bootstrapId,
                GetRequiredString(payload, "bootstrapSecret"),
                ParseDateTimeOffset(bootstrapExpiresAt));
        }

        private async Task<bool> TryRegisterPublicKeyAsync(string envelopeUrl, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            if (nextRegistrationAttemptUtc.HasValue && now < nextRegistrationAttemptUtc.Value)
            {
                if (!string.IsNullOrWhiteSpace(pendingRegistrationBackoffMessage))
                {
                    diagnostics?.Invoke(pendingRegistrationBackoffMessage);
                    pendingRegistrationBackoffMessage = null;
                }
                return false;
            }

            using var machineRsa = VHDManager.EnsureOrCreateTpmRsa(machineId);
            var publicKeyPem = VHDManager.ExportPublicKeyPem(machineRsa);
            using var registrationCertificate = VHDManager.LoadRegistrationCertificate(LoadConfigValues(configuration.ConfigPath));
            using var registrationPrivateKey = registrationCertificate.GetRSAPrivateKey();
            if (registrationPrivateKey == null)
            {
                throw new InvalidOperationException("注册证书不包含 RSA 私钥");
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var nonce = VHDManager.GenerateRegistrationNonce();
            var normalizedPublicKeyPem = VHDManager.NormalizePemText(publicKeyPem);
            var signingPayload = VHDManager.BuildRegistrationSigningPayload(
                machineId,
                keyId,
                "RSA",
                normalizedPublicKeyPem,
                timestamp,
                nonce);
            var signatureBytes = registrationPrivateKey.SignData(
                Encoding.UTF8.GetBytes(signingPayload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var baseUri = ResolveServiceBaseUri(envelopeUrl);
            var requestUri = new Uri(baseUri, $"/api/machines/{Uri.EscapeDataString(machineId)}/keys");
            var payload = new
            {
                keyId,
                keyType = "RSA",
                pubkeyPem = normalizedPublicKeyPem,
                registrationCertificatePem = ExportCertificatePem(registrationCertificate),
                signature = Convert.ToBase64String(signatureBytes),
                timestamp,
                nonce,
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload, jsonOptions), Encoding.UTF8, "application/json");
            using var response = await SharedHttpClient.PostAsync(requestUri, content, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                nextRegistrationAttemptUtc = null;
                pendingRegistrationBackoffMessage = null;
                diagnostics?.Invoke("已提交机台公钥签名注册，等待管理员审批");
                return true;
            }

            var retryDelay = TryGetRetryAfter(response, body) ?? TimeSpan.FromMinutes(1);
            nextRegistrationAttemptUtc = DateTimeOffset.UtcNow.Add(retryDelay);
            pendingRegistrationBackoffMessage = $"机台公钥注册失败，{FormatRetryDelay(retryDelay)}后重试: {ParseResponseError(body)}";
            diagnostics?.Invoke(pendingRegistrationBackoffMessage);
            return false;
        }

        private static async Task SendTextMessageAsync(ClientWebSocket webSocket, string text, CancellationToken cancellationToken)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(text);
            await webSocket.SendAsync(new ArraySegment<byte>(payloadBytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                using var ms = new MemoryStream();
                while (true)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, webSocket.CloseStatusDescription ?? "服务端已关闭连接");
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static string SerializeEncryptedFrameJson(byte[] sessionKey, object payload, long seq, long acknowledgedSeq)
        {
            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });
            var iv = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[16];
            using var aesGcm = new AesGcm(sessionKey, 16);
            aesGcm.Encrypt(iv, plaintextBytes, ciphertext, tag);

            return JsonSerializer.Serialize(new
            {
                type = "encrypted_frame",
                seq,
                ack = acknowledgedSeq,
                iv = Convert.ToBase64String(iv),
                ciphertext = Convert.ToBase64String(ciphertext),
                tag = Convert.ToBase64String(tag),
            });
        }

        private static string DecryptFramePayload(byte[] sessionKey, JsonElement frame)
        {
            var iv = Convert.FromBase64String(GetRequiredString(frame, "iv"));
            var ciphertext = Convert.FromBase64String(GetRequiredString(frame, "ciphertext"));
            var tag = Convert.FromBase64String(GetRequiredString(frame, "tag"));
            var plaintext = new byte[ciphertext.Length];
            using var aesGcm = new AesGcm(sessionKey, 16);
            aesGcm.Decrypt(iv, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }

        private static byte[] ExportEcPoint(ECParameters parameters)
        {
            if (parameters.Q.X == null || parameters.Q.Y == null)
            {
                throw new InvalidOperationException("ECDH 公钥参数无效");
            }

            var point = new byte[1 + parameters.Q.X.Length + parameters.Q.Y.Length];
            point[0] = 0x04;
            Buffer.BlockCopy(parameters.Q.X, 0, point, 1, parameters.Q.X.Length);
            Buffer.BlockCopy(parameters.Q.Y, 0, point, 1 + parameters.Q.X.Length, parameters.Q.Y.Length);
            return point;
        }

        private static ECDiffieHellmanPublicKey ImportRemotePublicKey(string publicKeyBase64)
        {
            var rawPoint = Convert.FromBase64String(publicKeyBase64);
            if (rawPoint.Length != 65 || rawPoint[0] != 0x04)
            {
                throw new InvalidOperationException("服务端 ECDH 公钥格式无效");
            }

            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(rawPoint, 1, x, 0, 32);
            Buffer.BlockCopy(rawPoint, 33, y, 0, 32);
            var remote = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = x,
                    Y = y,
                },
            });
            return remote.PublicKey;
        }

        private static DerivedKeys DeriveSessionKeys(byte[] sharedSecret, string bootstrapSecretBase64, string clientNonce, string serverNonce)
        {
            var bootstrapSecretBytes = Convert.FromBase64String(bootstrapSecretBase64);
            var ikm = new byte[sharedSecret.Length + bootstrapSecretBytes.Length];
            Buffer.BlockCopy(sharedSecret, 0, ikm, 0, sharedSecret.Length);
            Buffer.BlockCopy(bootstrapSecretBytes, 0, ikm, sharedSecret.Length, bootstrapSecretBytes.Length);
            var salt = Encoding.UTF8.GetBytes(clientNonce + serverNonce);

            return new DerivedKeys(
                HkdfSha256(ikm, salt, Encoding.UTF8.GetBytes("machine-log-ws-auth-v1"), 32),
                HkdfSha256(ikm, salt, Encoding.UTF8.GetBytes("machine-log-ws-data-v1"), 32));
        }

        private static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            using var extractHmac = new HMACSHA256(salt);
            var prk = extractHmac.ComputeHash(ikm);
            using var expandHmac = new HMACSHA256(prk);

            var output = new byte[length];
            var previousBlock = Array.Empty<byte>();
            var outputOffset = 0;
            byte counter = 1;

            while (outputOffset < length)
            {
                expandHmac.Initialize();
                var input = new byte[previousBlock.Length + info.Length + 1];
                Buffer.BlockCopy(previousBlock, 0, input, 0, previousBlock.Length);
                Buffer.BlockCopy(info, 0, input, previousBlock.Length, info.Length);
                input[input.Length - 1] = counter;
                previousBlock = expandHmac.ComputeHash(input);

                var bytesToCopy = Math.Min(previousBlock.Length, length - outputOffset);
                Buffer.BlockCopy(previousBlock, 0, output, outputOffset, bytesToCopy);
                outputOffset += bytesToCopy;
                counter += 1;
            }

            return output;
        }

        private static byte[] HashTranscript(
            string protocolVersion,
            string machineId,
            string keyId,
            string sessionId,
            string bootstrapId,
            long timestamp,
            string clientNonce,
            string clientEcdhPublicKey,
            MachineLogServerHello serverHello)
        {
            var transcript = string.Join("\n", new[]
            {
                "VHDMounterMachineLogTranscriptV1",
                protocolVersion,
                machineId,
                keyId,
                sessionId,
                bootstrapId,
                timestamp.ToString(CultureInfo.InvariantCulture),
                clientNonce,
                clientEcdhPublicKey,
                serverHello.ConnectionId,
                serverHello.Timestamp.ToString(CultureInfo.InvariantCulture),
                serverHello.Nonce,
                serverHello.ServerEcdhPublicKey,
                serverHello.HeartbeatSeconds.ToString(CultureInfo.InvariantCulture),
                serverHello.HeartbeatTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                serverHello.ReconnectBaseMs.ToString(CultureInfo.InvariantCulture),
                serverHello.ReconnectMaxMs.ToString(CultureInfo.InvariantCulture),
                serverHello.ResumeWindowSeconds.ToString(CultureInfo.InvariantCulture),
                serverHello.AcknowledgedSeq.ToString(CultureInfo.InvariantCulture),
            });

            return SHA256.HashData(Encoding.UTF8.GetBytes(transcript));
        }

        private static string ComputeFinishMac(byte[] authKey, byte[] transcriptHash, string label)
        {
            using var hmac = new HMACSHA256(authKey);
            hmac.TransformBlock(transcriptHash, 0, transcriptHash.Length, null, 0);
            hmac.TransformFinalBlock(Encoding.UTF8.GetBytes(label), 0, label.Length);
            return Convert.ToBase64String(hmac.Hash);
        }

        private static string BuildMachineLogHelloSigningPayload(
            string protocolVersion,
            string machineId,
            string keyId,
            string sessionId,
            string bootstrapId,
            long timestamp,
            string nonce,
            string clientEcdhPublicKey)
        {
            return string.Join("\n", new[]
            {
                "VHDMounterMachineLogHelloV1",
                protocolVersion.Trim(),
                machineId.Trim(),
                keyId.Trim(),
                sessionId.Trim(),
                bootstrapId.Trim(),
                timestamp.ToString(CultureInfo.InvariantCulture),
                nonce.Trim(),
                clientEcdhPublicKey.Trim(),
            });
        }

        private static string GenerateNonce()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }

        private static MachineLogServerHello ParseServerHello(JsonElement root, string expectedBootstrapId)
        {
            if (!TryGetString(root, "type", out var messageType) ||
                !string.Equals(messageType, "server_hello", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("服务端未返回 server_hello");
            }

            var bootstrapId = GetRequiredString(root, "bootstrapId");
            if (!string.Equals(bootstrapId, expectedBootstrapId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("server_hello 中的 bootstrapId 不匹配");
            }

            return new MachineLogServerHello(
                GetRequiredString(root, "connectionId"),
                GetRequiredString(root, "serverEcdhPublicKey"),
                GetRequiredString(root, "nonce"),
                GetInt64(root, "timestamp"),
                GetInt32(root, "heartbeatSeconds"),
                GetInt32(root, "heartbeatTimeoutSeconds"),
                GetInt32(root, "reconnectBaseMs"),
                GetInt32(root, "reconnectMaxMs"),
                GetInt32(root, "resumeWindowSeconds"),
                GetInt64(root, "acknowledgedSeq"));
        }

        private static async Task WaitForAcknowledgementAsync(
            long targetSeq,
            Func<long> currentAcknowledgedSeq,
            Func<string> currentCloseReason,
            SemaphoreSlim signal,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            while (currentAcknowledgedSeq() < targetSeq)
            {
                var closeReason = currentCloseReason();
                if (!string.IsNullOrWhiteSpace(closeReason))
                {
                    throw new InvalidOperationException(closeReason);
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"等待 ACK 超时，目标 seq={targetSeq}");
                }

                await signal.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task CloseConnectionSilentlyAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }
        }

        private static void ReleaseSignal(SemaphoreSlim signal)
        {
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static string ParseResponseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "unknown";
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                return TryGetString(doc.RootElement, "error", out var error)
                    ? MachineLogSanitizer.SanitizeSensitiveText(error)
                    : MachineLogSanitizer.SanitizeSensitiveText(body);
            }
            catch
            {
                return MachineLogSanitizer.SanitizeSensitiveText(body);
            }
        }

        private static DateTimeOffset ParseDateTimeOffset(string value)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                throw new InvalidOperationException($"时间格式无效: {value}");
            }

            return parsed;
        }

        private static Uri ResolveServiceBaseUri(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"服务地址无效: {url}");
            }

            return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
        }

        private static Dictionary<string, string> LoadConfigValues(string configPath)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(configPath))
            {
                return values;
            }

            foreach (var rawLine in File.ReadAllLines(configPath))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("["))
                {
                    continue;
                }

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    values[parts[0].Trim()] = parts[1].Trim();
                }
            }

            return values;
        }

        private static string ExportCertificatePem(X509Certificate2 certificate)
        {
            var certBytes = certificate.Export(X509ContentType.Cert);
            var b64 = Convert.ToBase64String(certBytes);
            var builder = new StringBuilder();
            builder.AppendLine("-----BEGIN CERTIFICATE-----");
            for (var index = 0; index < b64.Length; index += 64)
            {
                builder.AppendLine(b64.Substring(index, Math.Min(64, b64.Length - index)));
            }
            builder.AppendLine("-----END CERTIFICATE-----");
            return builder.ToString().Trim();
        }

        private static TimeSpan? TryGetRetryAfter(HttpResponseMessage response, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var retryAfterSecondsElement) &&
                    retryAfterSecondsElement.ValueKind == JsonValueKind.Number &&
                    retryAfterSecondsElement.TryGetInt32(out var retryAfterSeconds) &&
                    retryAfterSeconds > 0)
                {
                    return TimeSpan.FromSeconds(retryAfterSeconds);
                }
            }
            catch
            {
            }

            if (response?.Headers?.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
            {
                return retryAfter;
            }

            if (response != null && response.Headers.TryGetValues("RateLimit-Reset", out var rateLimitResetValues))
            {
                var rawValue = rateLimitResetValues.FirstOrDefault();
                if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var resetSeconds) && resetSeconds > 0)
                {
                    return TimeSpan.FromSeconds(resetSeconds);
                }
            }

            return null;
        }

        private static string FormatRetryDelay(TimeSpan delay)
        {
            if (delay.TotalMinutes >= 1)
            {
                return $"{Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes))} 分钟";
            }

            return $"{Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))} 秒";
        }

        private static async Task DelayForReconnectAsync(int reconnectBaseMs, int reconnectMaxMs, int failures, CancellationToken cancellationToken)
        {
            if (failures <= 0)
            {
                return;
            }

            var exponent = Math.Max(0, failures - 1);
            var computed = reconnectBaseMs * Math.Pow(2, exponent);
            var bounded = Math.Min(computed, reconnectMaxMs);
            var jitterFactor = 0.85 + (Random.Shared.NextDouble() * 0.3);
            var delayMs = Math.Max(reconnectBaseMs, (int)(bounded * jitterFactor));
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        private static int GetInt32(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property) || !property.TryGetInt32(out var value))
            {
                throw new InvalidOperationException($"响应缺少有效的整数属性: {propertyName}");
            }

            return value;
        }

        private static long GetInt64(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                throw new InvalidOperationException($"响应缺少属性: {propertyName}");
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var numericValue))
            {
                return numericValue;
            }

            if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
            {
                return stringValue;
            }

            throw new InvalidOperationException($"响应中的属性不是有效的长整数: {propertyName}");
        }

        private static string GetRequiredString(JsonElement root, string propertyName)
        {
            return TryGetString(root, propertyName, out var value)
                ? value
                : throw new InvalidOperationException($"响应缺少字符串属性: {propertyName}");
        }

        private static bool TryGetString(JsonElement root, string propertyName, out string value)
        {
            if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }

            value = string.Empty;
            return false;
        }

        private sealed record MachineLogBootstrapToken(string BootstrapId, string BootstrapSecret, DateTimeOffset ExpiresAt);

        private sealed record MachineLogServerHello(
            string ConnectionId,
            string ServerEcdhPublicKey,
            string Nonce,
            long Timestamp,
            int HeartbeatSeconds,
            int HeartbeatTimeoutSeconds,
            int ReconnectBaseMs,
            int ReconnectMaxMs,
            int ResumeWindowSeconds,
            long AcknowledgedSeq);

        private sealed record DerivedKeys(byte[] AuthKey, byte[] SessionKey);

        private sealed record HandshakeState(MachineLogServerHello ServerHello, byte[] AuthKey, byte[] SessionKey);

        private sealed record ConnectionRunResult(int ReconnectBaseMs, int ReconnectMaxMs, bool SwitchSessionImmediately);
    }
}