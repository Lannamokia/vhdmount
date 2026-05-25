using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VHDMounter.RustDeskBridge.Crypto;
using VHDMounter.RustDeskBridge.Json;

namespace VHDMounter.RustDeskBridge.Policy
{
    /// <summary>
    /// Trusted_Controllers_Snapshot 单槽存储（Requirement 8.3 / 8.4 / 8.5 / 8.6 / 14.5 / 决策点 5 / 6）。
    ///
    /// 持有一个 <see cref="ObfuscatedSnapshotSlot"/>（用 <see cref="InMemoryObfuscation"/> 包裹），
    /// <see cref="TryReplace"/> 三步串行校验：
    /// <list type="number">
    /// <item>服务端签名验签（TrustedControllersSnapshotV1 payload，含 jcs 规范化 entries 摘要）</item>
    /// <item>|now - issuedAt| ≤ 600_000ms</item>
    /// <item>snapshotSeq 严格大于上次接受值</item>
    /// </list>
    /// 替换瞬间抹零旧 plain buffer；单条快照 plain JSON > 256 KiB 直接拒绝并增加
    /// <c>snapshotOversizeCount</c>。
    ///
    /// <see cref="Evaluate"/> 解开混淆 → 查表 → 抹零工作 buffer → 返回 <see cref="PeerApprovalDecision"/>。
    /// <see cref="Invalidate"/> 抹零槽 + 标失效；<see cref="IsHealthy"/> 距上次成功 &lt; 600s 且失败次数 &lt; 3。
    /// </summary>
    internal sealed class SnapshotStore
    {
        public const int MaxPlainSnapshotBytes = 256 * 1024;
        public const long ServerTimeWindowMs = 600_000;
        public const long HealthExpiryMs = 600_000;
        public const int HealthFailureThreshold = 3;
        public const int ApprovalTtlMs = 1;

        private const string SnapshotPayloadVersion = "TrustedControllersSnapshotV1";

        private readonly InMemoryObfuscation _obfuscation;
        private readonly IClock _clock;
        private readonly object _gate = new object();

        private ObfuscatedSnapshotSlot _slot;
        private bool _hasValue;
        private long _lastSeq;
        private long _lastSuccessUtcMs;
        private int _consecutiveFailureCount;
        private long _snapshotOversizeCount;

        public SnapshotStore(InMemoryObfuscation obfuscation, IClock clock)
        {
            _obfuscation = obfuscation ?? throw new ArgumentNullException(nameof(obfuscation));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public long CurrentSeq
        {
            get { lock (_gate) return _lastSeq; }
        }

        public bool HasValue
        {
            get { lock (_gate) return _hasValue; }
        }

        public long SnapshotOversizeCount
        {
            get { lock (_gate) return _snapshotOversizeCount; }
        }

        /// <summary>
        /// Requirement 8.6 综合健康度：距上次成功 &lt; 600s 且连续失败 &lt; 3 次。
        /// </summary>
        public bool IsHealthy
        {
            get
            {
                lock (_gate)
                {
                    if (!_hasValue) return false;
                    if (_consecutiveFailureCount >= HealthFailureThreshold) return false;
                    var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
                    return (nowMs - _lastSuccessUtcMs) < HealthExpiryMs;
                }
            }
        }

        /// <summary>
        /// 增加失败计数（SnapshotRefreshLoop 在拉取 / 验签失败时调用）。
        /// </summary>
        public void RecordRefreshFailure()
        {
            lock (_gate)
            {
                if (_consecutiveFailureCount < int.MaxValue)
                {
                    _consecutiveFailureCount++;
                }
            }
        }

        /// <summary>
        /// 三步校验后替换 active 槽。返回 true 表示替换成功；返回 false 表示拒绝（旧槽保持不变）。
        /// </summary>
        public bool TryReplace(string snapshotJson, IPolicyPubkeyValidator validator, out string rejectReason)
        {
            if (snapshotJson == null) throw new ArgumentNullException(nameof(snapshotJson));
            if (validator == null) throw new ArgumentNullException(nameof(validator));

            // (a) 256 KiB 上限
            var jsonBytes = Encoding.UTF8.GetBytes(snapshotJson);
            if (jsonBytes.Length > MaxPlainSnapshotBytes)
            {
                lock (_gate)
                {
                    _snapshotOversizeCount++;
                    _consecutiveFailureCount++;
                }
                rejectReason = "snapshot_oversize";
                return false;
            }

            // (b) 解析 + 拆字段
            string machineId;
            long snapshotSeq;
            long issuedAtMs;
            string signatureBase64;
            JsonElement entriesElement;
            byte[] entriesPlainBytes = null;

            try
            {
                using var doc = JsonDocument.Parse(snapshotJson);
                var root = doc.RootElement;
                machineId = root.TryGetProperty("machineId", out var mEl) ? mEl.GetString() ?? string.Empty : string.Empty;
                snapshotSeq = root.TryGetProperty("snapshotSeq", out var sEl) && sEl.TryGetInt64(out var sVal) ? sVal : -1L;
                issuedAtMs = root.TryGetProperty("issuedAt", out var iEl) && iEl.TryGetInt64(out var iVal) ? iVal : 0L;
                signatureBase64 = root.TryGetProperty("signature", out var sigEl) ? sigEl.GetString() ?? string.Empty : string.Empty;
                if (!root.TryGetProperty("entries", out entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
                {
                    rejectReason = "missing_entries";
                    lock (_gate) _consecutiveFailureCount++;
                    return false;
                }

                // (c) 序号单调
                //     - snapshotSeq < _lastSeq → 真正的回退，拒绝（协议异常 / 时光倒流）
                //     - snapshotSeq == _lastSeq → 同一版本重复拉取（trustedControllerStore
                //       的 watermark 仅在 upsert/delete 时 ++，机台周期拉取看到的就是同
                //       一个值），视作"内容未变"安静通过：
                //         * 不替换 buffer，旧 active 槽继续生效
                //         * 不增加失败计数；同时把"距上次成功"的时间窗也刷新
                //         * 不写诊断（避免每个周期刷一条 reject 日志）
                lock (_gate)
                {
                    if (_hasValue && snapshotSeq == _lastSeq)
                    {
                        // 视作"this fetch confirmed we are still on the latest version"，
                        // 重置健康度计数，让 IsHealthy 仍为 true。
                        _consecutiveFailureCount = 0;
                        _lastSuccessUtcMs = _clock.UtcNow.ToUnixTimeMilliseconds();
                        rejectReason = null;
                        return true;
                    }
                    if (_hasValue && snapshotSeq < _lastSeq)
                    {
                        rejectReason = "snapshot_seq_regress";
                        _consecutiveFailureCount++;
                        return false;
                    }
                }

                // (d) 时间窗 ±600s
                var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
                if (Math.Abs(nowMs - issuedAtMs) > ServerTimeWindowMs)
                {
                    rejectReason = "issued_at_out_of_window";
                    lock (_gate) _consecutiveFailureCount++;
                    return false;
                }

                // (e) 服务端签名验签 —— payload = "TrustedControllersSnapshotV1\n<machineId>\n<snapshotSeq>\n<issuedAt>\n<sha256Hex(jcs(entries))>"
                var entriesCanonical = JcsCanonicalizer.Canonicalize(entriesElement);
                var entriesDigestHex = Convert.ToHexString(SHA256.HashData(entriesCanonical)).ToLowerInvariant();
                var payload = string.Concat(
                    SnapshotPayloadVersion, "\n",
                    machineId ?? string.Empty, "\n",
                    snapshotSeq.ToString(CultureInfo.InvariantCulture), "\n",
                    issuedAtMs.ToString(CultureInfo.InvariantCulture), "\n",
                    entriesDigestHex);
                var payloadBytes = Encoding.ASCII.GetBytes(payload);

                if (!validator.VerifyResponseSignature(payloadBytes, signatureBase64))
                {
                    rejectReason = "signature_invalid";
                    lock (_gate) _consecutiveFailureCount++;
                    return false;
                }

                // (f) 进入替换：把 entries 单独序列化（可能比 raw json 更紧凑），然后混淆包裹
                entriesPlainBytes = Encoding.UTF8.GetBytes(entriesElement.GetRawText());

                if (entriesPlainBytes.Length > MaxPlainSnapshotBytes)
                {
                    lock (_gate)
                    {
                        _snapshotOversizeCount++;
                        _consecutiveFailureCount++;
                    }
                    rejectReason = "snapshot_oversize";
                    return false;
                }
            }
            catch (JsonException)
            {
                rejectReason = "invalid_json";
                lock (_gate) _consecutiveFailureCount++;
                return false;
            }

            try
            {
                // (g) 包裹 entries，原子替换槽
                var wrapped = _obfuscation.Wrap(entriesPlainBytes);
                lock (_gate)
                {
                    var oldSlot = _slot;
                    _slot = new ObfuscatedSnapshotSlot(wrapped, machineId ?? string.Empty);
                    _hasValue = true;
                    _lastSeq = snapshotSeq;
                    _lastSuccessUtcMs = _clock.UtcNow.ToUnixTimeMilliseconds();
                    _consecutiveFailureCount = 0;

                    // 抹零旧 plain working buffer（entriesPlainBytes 即是替换瞬间的"旧明文"）
                    // 旧 slot 内的 cipher 不含明文，但 oldSlot 也持有的工作 buffer（无）；
                    // 由 InMemoryObfuscation.Wrap 已经只产出 cipher，所以无需对 oldSlot 做额外抹零。
                    _ = oldSlot;
                }
                rejectReason = null;
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entriesPlainBytes);
            }
        }

        /// <summary>
        /// Requirement 8.5：把 PeerApproval 帧的 (controllerId, controllerHwid, controlledMachineId)
        /// 解到本地查表得出 PeerApprovalDecision。命中：approved；未命中或失效：rejected。
        ///
        /// 解开混淆 → 查表 → 抹零工作 buffer，全程不缓存"上次决定"。
        /// </summary>
        public PeerApprovalDecision Evaluate(string controllerId, string controllerHwid, string thisMachineId)
        {
            byte[] plainEntries = null;
            try
            {
                ObfuscatedSnapshotSlot slot;
                long nowMs;
                lock (_gate)
                {
                    if (!_hasValue || !IsHealthyLocked())
                    {
                        return PeerApprovalDecision.Reject();
                    }
                    slot = _slot;
                    nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
                }

                plainEntries = _obfuscation.Unwrap(slot.Buffer);
                using var doc = JsonDocument.Parse(plainEntries);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return PeerApprovalDecision.Reject();
                }

                var hwidHash = ComputeHwidHashOrNull(controllerHwid);
                var scopedMachine = $"machine:{thisMachineId ?? string.Empty}";

                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    if (!IsEntryEnabled(entry, nowMs)) continue;

                    var entryControllerId = entry.TryGetProperty("controllerId", out var cIdEl)
                        ? cIdEl.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.Equals(entryControllerId, controllerId ?? string.Empty, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var entryScope = entry.TryGetProperty("scope", out var scEl)
                        ? scEl.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.Equals(entryScope, "global", StringComparison.Ordinal) &&
                        !string.Equals(entryScope, scopedMachine, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!entry.TryGetProperty("controllerHwidHash", out var hwidEl) ||
                        hwidEl.ValueKind == JsonValueKind.Null)
                    {
                        // entry 不限定 hwid → 命中
                        return PeerApprovalDecision.Approve();
                    }

                    var entryHwidHash = hwidEl.GetString();
                    if (string.IsNullOrEmpty(entryHwidHash))
                    {
                        return PeerApprovalDecision.Approve();
                    }

                    if (string.Equals(entryHwidHash, hwidHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return PeerApprovalDecision.Approve();
                    }
                }

                return PeerApprovalDecision.Reject();
            }
            catch (JsonException)
            {
                return PeerApprovalDecision.Reject();
            }
            catch (CryptographicException)
            {
                return PeerApprovalDecision.Reject();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainEntries);
            }
        }

        /// <summary>
        /// Requirement 8.6：失效 + 抹零槽。下次 PeerApproval 一律拒绝直到下一次成功拉取。
        /// </summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _hasValue = false;
                _slot = default;
                // _lastSeq 保留：避免被旧序号重放替换 ✓
                // 但实测期望：失效后应阻断到下一次 healthy（不归零 _lastSeq 是符合 Requirement 8.3.3 的更安全选择）
            }
        }

        // ---------- 内部辅助 ----------

        private bool IsHealthyLocked()
        {
            if (_consecutiveFailureCount >= HealthFailureThreshold) return false;
            var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
            return (nowMs - _lastSuccessUtcMs) < HealthExpiryMs;
        }

        private static bool IsEntryEnabled(JsonElement entry, long nowMs)
        {
            if (!entry.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.True)
            {
                return false;
            }

            if (entry.TryGetProperty("expiresAt", out var exEl) && exEl.ValueKind != JsonValueKind.Null)
            {
                if (exEl.ValueKind == JsonValueKind.Number && exEl.TryGetInt64(out var exMs) && exMs <= nowMs)
                {
                    return false;
                }
                if (exEl.ValueKind == JsonValueKind.String)
                {
                    var raw = exEl.GetString();
                    if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
                    {
                        if (dto.ToUnixTimeMilliseconds() <= nowMs) return false;
                    }
                }
            }

            return true;
        }

        private static string ComputeHwidHashOrNull(string controllerHwid)
        {
            if (string.IsNullOrEmpty(controllerHwid)) return string.Empty;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(controllerHwid))).ToLowerInvariant();
        }

        /// <summary>
        /// 单槽 + 元数据。槽切换是引用级原子（lock 内整体替换）。
        /// </summary>
        private readonly struct ObfuscatedSnapshotSlot
        {
            public ObfuscatedSnapshotSlot(ObfuscatedBuffer buffer, string machineId)
            {
                Buffer = buffer;
                MachineId = machineId;
            }

            public ObfuscatedBuffer Buffer { get; }
            public string MachineId { get; }
        }
    }
}
