using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassFirewall
{
    public sealed class DeepInspector : IDisposable
    {
        private readonly DomainMatcher _matcher = new();

        private TcpListener? _httpListener;
        private TcpListener? _httpsListener;
        private CancellationTokenSource? _cts;

        public int HttpPort { get; }
        public int HttpsPort { get; }
        public bool IsRunning { get; private set; }

        /// <summary>是否启用 MITM（需根证书已装）。false 时 HTTPS 命中只断连</summary>
        public bool EnableMitm { get; set; }

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

        public void UpdateBlacklist(IEnumerable<string> domains) => _matcher.Update(domains);

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
                OnDebug?.Invoke($"HTTPS 监听已启动: 127.0.0.1:{HttpsPort}" +
                                (EnableMitm ? " [MITM 模式]" : " [断连模式]"));
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
                    linked.CancelAfter(TimeSpan.FromSeconds(8));

                    var stream = client.GetStream();

                    Stream replayStream = stream;
                    string? domain;

                    if (protocol == "HTTP")
                    {
                        domain = await ReadHttpHostAsync(stream, linked.Token);
                    }
                    else
                    {
                        var (sni, consumed) = await ReadTlsClientHelloAsync(stream, linked.Token);
                        domain = sni;

                        // ★ 关键：解析 SNI 时已经把 ClientHello 从 socket 里读走了。
                        //   必须把这些字节回放给后面的 SslStream，否则它会一直等待
                        //   ClientHello，直到超时（表现为 "The operation was canceled."）。
                        replayStream = new PrefixedStream(stream, consumed);
                    }

                    if (string.IsNullOrEmpty(domain))
                    {
                        if (DebugLogging)
                            OnDebug?.Invoke($"[{protocol}] 未能解析域名");
                        return;
                    }

                    if (DebugLogging)
                        OnDebug?.Invoke($"[{protocol}] 解析到域名: {domain}");

                    if (!IsBlocked(domain))
                    {
                        OnAllowed?.Invoke(protocol, domain);
                        return;
                    }

                    OnBlocked?.Invoke(protocol, domain);

                    if (protocol == "HTTP")
                    {
                        // 已是明文 HTTP，直接返回 403
                        await SendHttpForbiddenAsync(stream, domain, linked.Token);
                    }
                    else
                    {
                        // HTTPS
                        if (EnableMitm && CertificateAuthority.IsInstalled())
                        {
                            await HandleHttpsMitmAsync(replayStream, domain, linked.Token);
                        }
                        else
                        {
                            // 断连模式：直接关闭（TCP RST 让浏览器立即报错）
                            try { client.Client.LingerState = new LingerOption(true, 0); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (DebugLogging)
                        OnDebug?.Invoke($"[{protocol}] 处理连接 {remote} 异常: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // ---------------- HTTPS MITM ----------------

        private async Task HandleHttpsMitmAsync(Stream rawStream, string domain, CancellationToken token)
        {
            X509Certificate2? cert;
            try
            {
                cert = CertificateAuthority.IssueForDomain(domain);
                if (DebugLogging)
                    OnDebug?.Invoke($"[MITM] 已签发证书: {domain}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"签发证书失败: {ex.Message}");
                return;
            }

            var replay = rawStream as PrefixedStream;
            if (DebugLogging)
                OnDebug?.Invoke($"[MITM] 开始握手: {domain} | 回放缓冲 {replay?.PrefixLength ?? -1} 字节" +
                                (replay != null ? $", 首 8 字节: {replay.PrefixHex(8)}" : ""));

            using var ssl = new SslStream(rawStream, leaveInnerStreamOpen: false);

            try
            {
                var options = new SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                };

                await ssl.AuthenticateAsServerAsync(options, token);
                if (DebugLogging)
                    OnDebug?.Invoke($"[MITM] TLS 握手成功: {domain} | {replay?.Stats ?? "无回放缓冲"}");
            }
            catch (Exception ex)
            {
                // 客户端不信任我们的 CA、主动断开，或握手超时
                if (DebugLogging)
                    OnDebug?.Invoke(
                        $"[MITM] TLS 握手失败 {ex.GetType().Name}: {ex.Message} | " +
                        (replay?.Stats ?? "无回放缓冲"));
                return;
            }

            // 读一段 HTTP 请求（不解析，只是为了等浏览器发完请求头）
            try
            {
                var buf = new byte[4096];
                var readTask = ssl.ReadAsync(buf, token).AsTask();
                await Task.WhenAny(readTask, Task.Delay(2000, token));
            }
            catch { }

            // 返回 403 页面
            await SendHttpForbiddenAsync(ssl, domain, token);
        }

        // ---------------- 黑名单匹配 ----------------

        private bool IsBlocked(string domain) => _matcher.IsBlocked(domain);

        // ---------------- HTTP Host 解析 ----------------

        private static async Task<string?> ReadHttpHostAsync(Stream stream, CancellationToken token)
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

        // ---------------- 403 页面 ----------------

        private static async Task SendHttpForbiddenAsync(Stream stream, string host, CancellationToken token)
        {
            try
            {
                string html =
                    "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                    "<title>已被拦截</title>" +
                    "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                    "<style>" +
                    "*{box-sizing:border-box;margin:0;padding:0}" +
                    "body{font-family:'Microsoft YaHei','PingFang SC',sans-serif;" +
                    "background:linear-gradient(135deg,#667eea 0%,#764ba2 100%);" +
                    "min-height:100vh;display:flex;align-items:center;justify-content:center;padding:20px}" +
                    ".box{background:#fff;border-radius:20px;padding:60px 50px;max-width:520px;" +
                    "width:100%;box-shadow:0 20px 60px rgba(0,0,0,.3);text-align:center}" +
                    ".icon{font-size:72px;margin-bottom:20px;line-height:1}" +
                    "h1{color:#c0392b;font-size:24px;margin-bottom:16px;font-weight:600}" +
                    ".host{color:#666;font-family:Consolas,Monaco,monospace;font-size:13px;" +
                    "background:#f5f5f7;padding:10px 16px;border-radius:8px;" +
                    "display:inline-block;margin:8px 0 24px;word-break:break-all}" +
                    ".msg{color:#999;font-size:14px;line-height:1.7}" +
                    ".brand{margin-top:32px;padding-top:20px;border-top:1px solid #eee;" +
                    "color:#bbb;font-size:12px;letter-spacing:1px}" +
                    "</style></head><body>" +
                    "<div class=\"box\">" +
                    "<div class=\"icon\">🚫</div>" +
                    "<h1>该网站已被拦截</h1>" +
                    $"<div class=\"host\">{WebUtility.HtmlEncode(host)}</div>" +
                    "<div class=\"msg\">此网站属于 Class Firewall 的屏蔽列表。<br>" +
                    "如需解除屏蔽，请联系管理员。</div>" +
                    "<div class=\"brand\">CLASS FIREWALL</div>" +
                    "</div></body></html>";

                var body = Encoding.UTF8.GetBytes(html);
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Content-Length: " + body.Length + "\r\n" +
                    "Connection: close\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "\r\n");

                await stream.WriteAsync(head, token);
                await stream.WriteAsync(body, token);
                await stream.FlushAsync(token);
            }
            catch { }
        }

        // ---------------- TLS SNI 解析 ----------------

        /// <summary>
        /// 读取 TLS ClientHello 并解析 SNI。
        ///
        /// ★ 返回的 consumed 是本次从流中真正读走的全部字节。
        ///   调用方在进入 MITM 时**必须**把这些字节回放给 SslStream，
        ///   否则 SslStream 收不到 ClientHello，会一直阻塞到超时
        ///   （症状：TLS 握手失败 "The operation was canceled."）。
        ///
        /// 同时兼容 ClientHello 被拆分到多个 TLS 记录的情况。
        /// </summary>
        private static async Task<(string? sni, byte[] consumed)> ReadTlsClientHelloAsync(
            Stream stream, CancellationToken token)
        {
            const int maxRecords = 8;
            const int maxHandshake = 64 * 1024;

            var consumed = new MemoryStream();
            var handshake = new MemoryStream();
            var header = new byte[5];

            for (int i = 0; i < maxRecords; i++)
            {
                if (!await ReadExactAsync(stream, header, 5, token)) break;
                consumed.Write(header, 0, 5);

                if (header[0] != 0x16) break;   // 不是握手记录

                int recordLen = (header[3] << 8) | header[4];
                if (recordLen <= 0 || recordLen > 16384) break;

                var record = new byte[recordLen];
                if (!await ReadExactAsync(stream, record, recordLen, token)) break;

                consumed.Write(record, 0, recordLen);
                handshake.Write(record, 0, recordLen);

                // 握手消息是否已收全：前 4 字节 = 类型(1) + 长度(3)
                var hs = handshake.GetBuffer();
                if (handshake.Length >= 4)
                {
                    int hsLen = (hs[1] << 16) | (hs[2] << 8) | hs[3];
                    if (handshake.Length >= 4 + hsLen) break;
                }
                if (handshake.Length > maxHandshake) break;
            }

            var data = handshake.ToArray();
            string? sni = (data.Length >= 4 && data[0] == 0x01) ? ParseSni(data) : null;
            return (sni, consumed.ToArray());
        }

        /// <summary>从完整的 ClientHello 握手消息中解析 SNI</summary>
        private static string? ParseSni(byte[] record)
        {
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

        private static async Task<bool> ReadExactAsync(Stream stream, byte[] buf, int count, CancellationToken token)
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

        /// <summary>
        /// 只读"回放"包装流：先把 <c>prefix</c> 里的字节吐出来，之后透传到底层流。
        ///
        /// 用途：解析 SNI 时已从 socket 消费掉 ClientHello，MITM 握手前需要把
        /// 这些字节还回去，SslStream 才能正常完成握手。
        /// 写入/刷新直接透传；不拥有底层流，Dispose 不会关闭 socket。
        /// </summary>
        private sealed class PrefixedStream : Stream
        {
            private readonly Stream _inner;
            private readonly byte[] _prefix;
            private int _pos;

            // ---- 诊断计数：用来定位 MITM 卡在哪一步 ----
            public int PrefixLength => _prefix.Length;
            public int PrefixConsumed => _pos;
            public long SocketReads;
            public long SocketWrites;

            /// <summary>调用方发起的零长度读次数（SslStream 起手会来一次，必须就地返回 0）</summary>
            public long ZeroLengthReads;

            public string Stats =>
                $"回放 {_pos}/{_prefix.Length} 字节, 底层读取 {SocketReads} 次, " +
                $"已向客户端写出 {SocketWrites} 次, 零长读 {ZeroLengthReads} 次";

            /// <summary>回放缓冲的前 N 个字节的十六进制，用于确认它是不是一条合法的 TLS 记录</summary>
            public string PrefixHex(int n)
            {
                if (_prefix.Length == 0) return "(空)";
                int count = Math.Min(n, _prefix.Length);
                var parts = new string[count];
                for (int i = 0; i < count; i++) parts[i] = _prefix[i].ToString("X2");
                return string.Join(" ", parts);
            }

            public PrefixedStream(Stream inner, byte[]? prefix)
            {
                _inner = inner;
                _prefix = prefix ?? Array.Empty<byte>();
            }

            private int PrefixAvailable => _prefix.Length - _pos;

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => _inner.CanWrite;
            public override bool CanTimeout => _inner.CanTimeout;

            public override int ReadTimeout
            {
                get => _inner.ReadTimeout;
                set => _inner.ReadTimeout = value;
            }

            public override int WriteTimeout
            {
                get => _inner.WriteTimeout;
                set => _inner.WriteTimeout = value;
            }

            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            private int TakeFromPrefix(byte[] buffer, int offset, int count)
            {
                int avail = PrefixAvailable;
                if (avail <= 0) return 0;

                int n = Math.Min(avail, count);
                Array.Copy(_prefix, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }

            private int TakeFromPrefix(Memory<byte> buffer)
            {
                int avail = PrefixAvailable;
                if (avail <= 0) return 0;

                int n = Math.Min(avail, buffer.Length);
                _prefix.AsMemory(_pos, n).CopyTo(buffer);
                _pos += n;
                return n;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // ★ Stream 契约：零长度读必须立即返回 0，绝不能转发到底层。
                //   对 socket 做零长度读不会立刻返回，而是阻塞到有数据为止 ——
                //   SslStream 起手就有一次零长度探测读，转发过去会直接死锁。
                if (count <= 0) { ZeroLengthReads++; return 0; }

                int n = TakeFromPrefix(buffer, offset, count);
                if (n > 0) return n;
                SocketReads++;
                return _inner.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (count <= 0) { ZeroLengthReads++; return Task.FromResult(0); }

                int n = TakeFromPrefix(buffer, offset, count);
                if (n > 0) return Task.FromResult(n);
                SocketReads++;
                return _inner.ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (buffer.Length <= 0) { ZeroLengthReads++; return new ValueTask<int>(0); }

                int n = TakeFromPrefix(buffer);
                if (n > 0) return new ValueTask<int>(n);
                SocketReads++;
                return _inner.ReadAsync(buffer, ct);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count <= 0) return;
                SocketWrites++;
                _inner.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (count <= 0) return Task.CompletedTask;
                SocketWrites++;
                return _inner.WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                if (buffer.Length == 0) return ValueTask.CompletedTask;
                SocketWrites++;
                return _inner.WriteAsync(buffer, ct);
            }

            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            // 底层流由 HandleClientAsync 的 using(client) 负责关闭，这里不动它
            protected override void Dispose(bool disposing) { }
        }

        public void Dispose() => Stop();
    }
}