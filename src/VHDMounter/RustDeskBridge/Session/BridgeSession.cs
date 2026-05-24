using System;
using System.Collections.Generic;
using System.IO.Pipes;

namespace VHDMounter.RustDeskBridge.Session
{
    /// <summary>
    /// 单次 Bridge_Session 上下文（Requirement 12.3 / 12.4 / 14.5）。
    ///
    /// 持有：
    /// <list type="bullet">
    /// <item><see cref="Pipe"/>：NamedPipeServerStream 实例</item>
    /// <item><see cref="State"/>：当前阶段（Connecting / Handshaked / Closing）</item>
    /// <item><see cref="NegotiatedSecretVersion"/>：握手帧中校验通过的 secretVersion</item>
    /// <item><see cref="ReportNonces"/>：本 session 见过的 Report 帧 nonce 集合</item>
    /// <item><see cref="PeerApprovalNonces"/>：本 session 见过的 PeerApproval 帧 requestNonce 集合</item>
    /// </list>
    ///
    /// 任一 nonce HashSet ≥ <see cref="MaxNoncePerSession"/> → 关闭会话（Requirement 14.5）。
    /// 不写状态机逻辑（Wave 5 任务 9.4），仅是数据容器。
    /// </summary>
    internal sealed class BridgeSession : IDisposable
    {
        public const int MaxNoncePerSession = 4096;

        private readonly object _gate = new object();
        private readonly HashSet<string> _reportNonces = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _peerApprovalNonces = new HashSet<string>(StringComparer.Ordinal);
        private BridgeSessionState _state = BridgeSessionState.Connecting;
        private uint _negotiatedSecretVersion;
        private bool _disposed;

        public BridgeSession(NamedPipeServerStream pipe)
        {
            Pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public NamedPipeServerStream Pipe { get; }

        public DateTimeOffset CreatedAt { get; }

        public BridgeSessionState State
        {
            get { lock (_gate) return _state; }
        }

        public bool IsHandshaked
        {
            get { lock (_gate) return _state == BridgeSessionState.Handshaked; }
        }

        public uint NegotiatedSecretVersion
        {
            get { lock (_gate) return _negotiatedSecretVersion; }
        }

        public IReadOnlyCollection<string> ReportNonces
        {
            get { lock (_gate) return new List<string>(_reportNonces); }
        }

        public IReadOnlyCollection<string> PeerApprovalNonces
        {
            get { lock (_gate) return new List<string>(_peerApprovalNonces); }
        }

        public int ReportNonceCount
        {
            get { lock (_gate) return _reportNonces.Count; }
        }

        public int PeerApprovalNonceCount
        {
            get { lock (_gate) return _peerApprovalNonces.Count; }
        }

        /// <summary>
        /// 握手通过后由帧路由器调用：标记 Handshaked + 锁定 secretVersion。
        /// </summary>
        public void MarkHandshaked(uint secretVersion)
        {
            lock (_gate)
            {
                if (_state != BridgeSessionState.Connecting)
                {
                    throw new InvalidOperationException(
                        $"BridgeSession 状态非法：期望 Connecting，实际 {_state}");
                }
                _state = BridgeSessionState.Handshaked;
                _negotiatedSecretVersion = secretVersion;
            }
        }

        /// <summary>
        /// 把状态推进到 Closing（任意原因）。Idempotent。
        /// </summary>
        public void MarkClosing()
        {
            lock (_gate)
            {
                _state = BridgeSessionState.Closing;
            }
        }

        /// <summary>
        /// 记录 Report 帧 nonce。返回 true：首次见且未触达上限；false：重复或超限。
        /// 触达上限时 SHALL 同时返回 false 并附带 <paramref name="overflow"/> = true，
        /// 让上层关闭会话（Requirement 14.5）。
        /// </summary>
        public bool RecordReportNonce(string nonce, out bool overflow)
        {
            return RecordNonce(_reportNonces, nonce, out overflow);
        }

        public bool RecordPeerApprovalNonce(string nonce, out bool overflow)
        {
            return RecordNonce(_peerApprovalNonces, nonce, out overflow);
        }

        private bool RecordNonce(HashSet<string> set, string nonce, out bool overflow)
        {
            overflow = false;
            if (string.IsNullOrEmpty(nonce))
            {
                return false;
            }

            lock (_gate)
            {
                if (set.Count >= MaxNoncePerSession)
                {
                    overflow = true;
                    return false;
                }
                if (!set.Add(nonce))
                {
                    return false;
                }
                if (set.Count >= MaxNoncePerSession)
                {
                    overflow = true;
                }
                return true;
            }
        }

        /// <summary>
        /// 关闭管道句柄。Idempotent。
        /// </summary>
        public void Close()
        {
            if (_disposed) return;
            try
            {
                MarkClosing();
                if (Pipe.IsConnected)
                {
                    try { Pipe.Disconnect(); } catch { /* 已断开 */ }
                }
            }
            catch
            {
                // 关闭路径任何异常都不应抛出
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
            try { Pipe.Dispose(); } catch { /* 已释放 */ }
        }
    }
}
