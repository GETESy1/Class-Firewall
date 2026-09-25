using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassFirewall
{
    public sealed class DeepInspector : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _blacklist
            = new(StringComparer.OrdinalIgnoreCase);

        private TcpListener? _httpListener;
        private TcpListener? _httpsListener;
        private CancellationTokenSource? _cts;

        public int HttpPort { get; }
        public int HttpsPort { get; }
        public bool IsRunning { get; private set; }

        /// <summary>调试日志开关：开启后记录每次连接的详情</summary>
        public bool DebugLogging { get; set; } = true;

        public event Action<string, string>? OnBlocked;
        public event Action<string, string>? OnAllowed;
        public event Action<string>? OnError;
        public event Action<string>? OnDebug;

        public DeepInspector(int httpPort = 80, int httpsPort = 443)
        {
            HttpPort = httpPort;
            HttpsPort = httpsPort;
        }

        public void UpdateBlacklist(IEnumerable<string> domains)
        {
            _blacklist.Clear();
            foreach (var d in domains)
            {
                var s = d.Trim().ToLowerInvariant();
                if (s.StartsWith(".")) s = s.Substring(1);
                if (s.Length > 0) _blacklist[s] = 1;
            }
        }

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();

            _httpListener = new TcpListener(IPAddress.Loopback, HttpPort);
            _httpsListener = new TcpListener(IPAddress.Loopback, HttpsPort);

            try
            {
                _httpListener.Start();
                OnDebug?.Invoke($"HTTP 监听已启动: 127.0.0.1:{HttpPort}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"HTTP 监听 {HttpPort} 失败: {ex.Message}");
                throw;
            }

            try
            {
                _httpsListener.Start();
                OnDebug?.Invoke($"HTTPS 监听已启动: 127.0.0.1:{HttpsPort}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"HTTPS 监听 {HttpsPort} 失败: {ex.Message}");
                try { _httpListener.Stop(); } catch { }
                throw;
            }

            IsRunning = true;

            _ = Task.Run(() => AcceptLoopAsync(_httpListener, "HTTP", _cts.Token));
            _ = Task.Run(() => AcceptLoopAsync(_httpsListener, "HTTPS", _cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            try { _cts?.Cancel(); } catch { }
            try { _httpListener?.Stop(); } catch { }
            try { _httpsListener?.Stop(); } catch { }
            IsRunning = false;
        }

        private async Task AcceptLoopAsync(TcpListener listener, string protocol, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    OnError?.Invoke($"{protocol} 监听终止: {ex.Message}");
                    break;
                }
                _ = Task.Run(() => HandleClientAsync(client, protocol, token), token);
            }
        }

        private async Task HandleClientAsync(TcpClient client, string protocol, CancellationToken token)
        {
            using (client)
            {
                string remote = "?";
                try
                {
                    remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
                    if (DebugLogging)
                        OnDebug?.Invoke($"[{protocol}] 收到连接来自 {remote}");

                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                    linked.CancelAfter(TimeSpan.FromSeconds(6));

                    var stream = client.GetStream();

                    string? domain = protocol == "HTTP"
                        ? await ReadHttpHostAsync(stream, linked.Token)
                        : await ReadTlsSniAsync(stream, linked.Token);

                    if (string.IsNullOrEmpty(domain))
                    {
                        if (DebugLogging)
                            OnDebug?.Invoke($"[{protocol}] 未能解析域名（可能不是标准 HTTP/TLS）");
                        return;
                    }

                    if (DebugLogging)
                        OnDebug?.Invoke($"[{protocol}] 解析到域名: {domain}");

                    if (IsBlocked(domain))
                    {
                        OnBlocked?.Invoke(protocol, domain);

                        if (protocol == "HTTP")
                            await TrySendHttpForbiddenAsync(stream, domain, linked.Token);

                        // HTTPS 命中：不写任何数据，直接关闭 → 浏览器 ERR_CONNECTION_RESET
                        return;
                    }

                    OnAllowed?.Invoke(protocol, domain);
                }
                catch (Exception ex)
                {
                    if (DebugLogging)
                        OnDebug?.Invoke($"[{protocol}] 处理连接 {remote} 异常: {ex.Message}");
                }
            }
        }

        /// <summary>后缀匹配（已修复：跳过前导点）</summary>
        private bool IsBlocked(string domain)
        {
            domain = domain.TrimEnd('.').ToLowerInvariant();

            if (_blacklist.ContainsKey(domain)) return true;

            int idx = domain.IndexOf('.');
            while (idx > 0)
            {
                var suffix = domain.Substring(idx + 1); // ★ 跳过点
                if (_blacklist.ContainsKey(suffix)) return true;
                idx = domain.IndexOf('.', idx + 1);
            }
            return false;
        }

        // ---------------- HTTP Host 解析 ----------------

        private static async Task<string?> ReadHttpHostAsync(NetworkStream stream, CancellationToken token)
        {
            var buf = new byte[8192];
            int total = 0;

            while (total < buf.Length)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), token);
                if (n <= 0) break;
                total += n;
                if (ContainsDoubleCrlf(buf, total)) break;
            }

            if (total == 0) return null;

            var text = Encoding.ASCII.GetString(buf, 0, total);
            foreach (var line in text.Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    var host = line.Substring(5).Trim();
                    int colon = host.IndexOf(':');
                    if (colon > 0) host = host.Substring(0, colon);
                    return host.ToLowerInvariant();
                }
            }
            return null;
        }

        private static bool ContainsDoubleCrlf(byte[] buf, int len)
        {
            for (int i = 0; i + 3 < len; i++)
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                    return true;
            return false;
        }

        private static async Task TrySendHttpForbiddenAsync(NetworkStream stream, string host, CancellationToken token)
        {
            try
            {
                string html =
                    "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                    "<title>已拦截</title>" +
                    "<style>body{font-family:'Microsoft YaHei',sans-serif;text-align:center;" +
                    "padding:80px;color:#333;background:#f7f7f9}" +
                    "h1{color:#c0392b;font-size:26px}" +
                    ".box{display:inline-block;padding:40px 60px;background:#fff;" +
                    "border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.08)}" +
                    ".host{color:#666;margin-top:12px;font-family:Consolas,monospace}</style>" +
                    "</head><body><div class=\"box\">" +
                    "<h1>🚫 该网站已被 Class Firewall 拦截</h1>" +
                    $"<div class=\"host\">{WebUtility.HtmlEncode(host)}</div>" +
                    "<p style=\"margin-top:20px;color:#999;font-size:13px\">" +
                    "如需解除屏蔽，请联系管理员。</p>" +
                    "</div></body></html>";

                var body = Encoding.UTF8.GetBytes(html);
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Connection: close\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "\r\n");

                await stream.WriteAsync(head, token);
                await stream.WriteAsync(body, token);
                await stream.FlushAsync(token);
            }
            catch { }
        }

        // ---------------- TLS SNI 解析 ----------------

        private static async Task<string?> ReadTlsSniAsync(NetworkStream stream, CancellationToken token)
        {
            var header = new byte[5];
            if (!await ReadExactAsync(stream, header, 5, token)) return null;
            if (header[0] != 0x16) return null;

            int recordLen = (header[3] << 8) | header[4];
            if (recordLen <= 0 || recordLen > 16384) return null;

            var record = new byte[recordLen];
            if (!await ReadExactAsync(stream, record, recordLen, token)) return null;
            if (record.Length < 4) return null;

            int pos = 0;
            byte handshakeType = record[pos++];
            if (handshakeType != 0x01) return null;
            pos += 3;

            if (pos + 2 + 32 + 1 > record.Length) return null;
            pos += 2 + 32;

            int sessionIdLen = record[pos++];
            if (pos + sessionIdLen + 2 > record.Length) return null;
            pos += sessionIdLen;

            int cipherLen = (record[pos] << 8) | record[pos + 1];
            pos += 2;
            if (pos + cipherLen + 1 > record.Length) return null;
            pos += cipherLen;

            int compLen = record[pos++];
            if (pos + compLen + 2 > record.Length) return null;
            pos += compLen;

            int extLen = (record[pos] << 8) | record[pos + 1];
            pos += 2;
            if (pos + extLen > record.Length) extLen = record.Length - pos;

            int end = pos + extLen;
            while (pos + 4 <= end)
            {
                int extType = (record[pos] << 8) | record[pos + 1];
                int extSize = (record[pos + 2] << 8) | record[pos + 3];
                pos += 4;

                if (pos + extSize > record.Length) break;

                if (extType == 0x0000)
                {
                    int p = pos;
                    if (p + 2 > record.Length) break;
                    p += 2;
                    if (p + 3 > record.Length) break;
                    p += 1;
                    int nameLen = (record[p] << 8) | record[p + 1];
                    p += 2;
                    if (p + nameLen > record.Length) break;
                    return Encoding.ASCII.GetString(record, p, nameLen).ToLowerInvariant();
                }
                pos += extSize;
            }
            return null;
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buf, int count, CancellationToken token)
        {
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buf.AsMemory(read, count - read), token);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public void Dispose() => Stop();
    }
}