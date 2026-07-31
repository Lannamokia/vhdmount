using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VHDMounter.RustDeskBridge.Config;
using VHDMounter.RustDeskBridge.Frames;

namespace VHDMounter.RustDeskBridge.Revocation
{
    /// <summary>
    /// 决策点 4：服务端 → 机台反向通道。<see cref="HttpListener"/> 仅监听
    /// <c>http://127.0.0.1:&lt;BridgeRevocationListenPort&gt;/rustdesk/revoke</c>（默认 7891）。
    ///
    /// <para>
    /// 信任模型：本 feature 假设 loopback 部署 / 内网路由，<b>不</b>对反向请求做签名验证；
    /// 仅校验请求来源是 loopback（<see cref="HttpListenerRequest.IsLocal"/>）。Windows 防火墙
    /// 规则禁止公网访问由部署文档说明，<b>不</b>在代码层强制 —— 但本类的 prefix 字面量
    /// 强制绑定 127.0.0.1，从协议栈一侧已经无法被远端到达。
    /// </para>
    ///
    /// <para>
    /// 请求体形如 <c>{"reason":"denied","issuedAt":1730...,"snapshotVersion":42}</c>；
    /// reason ∈ {<see cref="RevocationFrame.ReasonDenied"/>, <see cref="RevocationFrame.ReasonSecretOutdated"/>}。
    /// 收到后调 <see cref="RevocationPublisher.PushAsync"/>（去重 / 抹零快照 / 关闭管道由 publisher 内部处理）。
    /// </para>
    ///
    /// <para>
    /// HttpListener.Start 失败（端口被占用 / 权限不足）→ 退避 5s 后重试，避免 CPU 100%。
    /// </para>
    /// </summary>
    internal sealed class RevocationListener : IAsyncDisposable
    {
        public static readonly TimeSpan StartRetryBackoff = TimeSpan.FromSeconds(5);

        private readonly BridgeConfig _config;
        private readonly RevocationPublisher _publisher;
        private readonly Action<string> _diagnostics;

        private CancellationTokenSource _cts;
        private Task _runner;
        private HttpListener _listener;

        public RevocationListener(
            BridgeConfig config,
            RevocationPublisher publisher,
            Action<string> diagnostics = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _diagnostics = diagnostics ?? (_ => { });
        }

        public Task StartAsync(CancellationToken ct)
        {
            if (_runner != null)
            {
                throw new InvalidOperationException("RevocationListener 已经启动");
            }
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runner = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_cts == null) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }

            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }

            try
            {
                if (_runner != null)
                {
                    await _runner.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                _diagnostics($"RevocationListener 停止异常：{ex.Message}");
            }
            finally
            {
                try { _cts.Dispose(); } catch { /* ignore */ }
                _cts = null;
                _runner = null;
                _listener = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        // ---------- 内部 ----------

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListener listener = null;
                try
                {
                    listener = new HttpListener();
                    var prefix = $"http://127.0.0.1:{_config.BridgeRevocationListenPort}/rustdesk/revoke/";
                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    _listener = listener;
                    _diagnostics($"RevocationListener 监听 {prefix}");
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _diagnostics($"RevocationListener 启动失败：{ex.Message}，{(int)StartRetryBackoff.TotalSeconds}s 后重试");
                    try { listener?.Close(); } catch { /* ignore */ }
                    try { await Task.Delay(StartRetryBackoff, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    continue;
                }

                try
                {
                    await ServeLoopAsync(listener, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _diagnostics($"RevocationListener 主循环异常：{ex.Message}");
                }
                finally
                {
                    try { listener.Stop(); } catch { /* ignore */ }
                    try { listener.Close(); } catch { /* ignore */ }
                    _listener = null;
                }

                if (ct.IsCancellationRequested) return;

                // 主循环退出（端口被释放 / 异常）→ 短暂退避再重启
                try { await Task.Delay(StartRetryBackoff, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }

        private async Task ServeLoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    var getContextTask = listener.GetContextAsync();
                    var completed = await Task.WhenAny(
                        getContextTask,
                        Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
                    if (completed != getContextTask)
                    {
                        return; // 取消
                    }
                    context = await getContextTask.ConfigureAwait(false);
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995 /* ERROR_OPERATION_ABORTED */)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = HandleRequestAsync(context, ct);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // 仅接受 loopback 来源（防御 prefix 配置错误地绑到 0.0.0.0 等情况）
                if (!request.IsLocal)
                {
                    response.StatusCode = (int)HttpStatusCode.Forbidden;
                    response.Close();
                    return;
                }

                if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    response.Close();
                    return;
                }

                if (!string.Equals(request.Url?.AbsolutePath?.TrimEnd('/'),
                                   "/rustdesk/revoke",
                                   StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Close();
                    return;
                }

                string bodyText;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    bodyText = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                }

                var reason = TryReadReasonOrDefault(bodyText);
                if (string.IsNullOrEmpty(reason))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    var msg = Encoding.UTF8.GetBytes("{\"success\":false,\"error\":\"缺少 reason 字段或 reason 非法\"}");
                    response.ContentType = "application/json; charset=utf-8";
                    await response.OutputStream.WriteAsync(msg, ct).ConfigureAwait(false);
                    response.Close();
                    return;
                }

                _diagnostics($"RevocationListener 收到反向通知 reason={reason}");

                // 异步推送 —— 不阻塞 HTTP 响应
                response.StatusCode = (int)HttpStatusCode.Accepted;
                response.ContentType = "application/json; charset=utf-8";
                var ack = Encoding.UTF8.GetBytes("{\"success\":true,\"accepted\":true}");
                await response.OutputStream.WriteAsync(ack, ct).ConfigureAwait(false);
                response.Close();

                try
                {
                    await _publisher.PushAsync(reason, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnostics($"RevocationListener 调 PushAsync 异常：{ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _diagnostics($"RevocationListener 处理请求异常：{ex.Message}");
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch { /* ignore */ }
            }
        }

        private static string TryReadReasonOrDefault(string bodyText)
        {
            if (string.IsNullOrWhiteSpace(bodyText)) return null;
            try
            {
                using var doc = JsonDocument.Parse(bodyText);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!doc.RootElement.TryGetProperty("reason", out var reasonEl)
                    || reasonEl.ValueKind != JsonValueKind.String)
                {
                    return null;
                }
                var reason = reasonEl.GetString();
                if (string.Equals(reason, RevocationFrame.ReasonDenied, StringComparison.Ordinal)
                    || string.Equals(reason, RevocationFrame.ReasonSecretOutdated, StringComparison.Ordinal))
                {
                    return reason;
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
