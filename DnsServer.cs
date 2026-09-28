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
    public sealed class DnsServer : IDisposable
    {
        private const int SIO_UDP_CONNRESET = -1744830452;

        private readonly DomainMatcher _matcher = new();

        private readonly ConcurrentDictionary<string, CacheEntry> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        private sealed class CacheEntry
        {
            public byte[] Response = Array.Empty<byte>();
            public DateTime ExpiresAt;
        }

        private static readonly IPEndPoint[] Upstreams =
        {
            new IPEndPoint(IPAddress.Parse("223.5.5.5"), 53),
            new IPEndPoint(IPAddress.Parse("114.114.114.114"), 53),
            new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53)
        };

        private UdpClient? _listener;
        private CancellationTokenSource? _cts;

        public int Port { get; }
        public bool IsRunning { get; private set; }
        public long QueryCount { get; private set; }
        public long BlockedCount { get; private set; }
        public long CacheHits { get; private set; }

        public event Action<string>? OnBlocked;
        public event Action<string>? OnError;

        public DnsServer(int port = 53) { Port = port; }

        public void UpdateBlacklist(IEnumerable<string> domains) => _matcher.Update(domains);

        public void Start()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            _listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
            try
            {
                _listener.Client.IOControl(
                    (IOControlCode)SIO_UDP_CONNRESET,
                    new byte[] { 0, 0, 0, 0 },
                    null);
            }
            catch {  }

            IsRunning = true;
            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Close(); } catch { }
            IsRunning = false;
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult req;
                try
                {
                    req = await _listener!.ReceiveAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException ex)
                {

                    if (ex.SocketErrorCode == SocketError.ConnectionReset ||
                        ex.SocketErrorCode == SocketError.ConnectionRefused ||
                        ex.SocketErrorCode == SocketError.NetworkReset ||
                        ex.SocketErrorCode == SocketError.MessageSize)
                    {
                        continue;
                    }
                    OnError?.Invoke("DNS 监听终止: " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke("DNS 监听终止: " + ex.Message);
                    break;
                }

                _ = Task.Run(() => HandleQueryAsync(req), token);
            }
        }

        private async Task HandleQueryAsync(UdpReceiveResult req)
        {
            try
            {
                QueryCount++;
                var buf = req.Buffer;
                if (buf.Length < 12) return;

                int qnameStart = 12;
                int qnameEnd = GetQNameEnd(buf, qnameStart);
                if (qnameEnd < 0 || qnameEnd + 4 > buf.Length) return;

                string qname = ParseQName(buf, qnameStart, out _);
                if (string.IsNullOrEmpty(qname)) return;

      
                if (IsBlacklisted(qname))
                {
                    BlockedCount++;
                    OnBlocked?.Invoke(qname);
                    var resp = BuildBlockedResponse(buf, qnameEnd + 4);
                    await SafeSendAsync(resp, req.RemoteEndPoint);
                    return;
                }

                if (_cache.TryGetValue(qname, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
                {
                    CacheHits++;
                    var cached = (byte[])entry.Response.Clone();
                    cached[0] = buf[0];
                    cached[1] = buf[1];
                    await SafeSendAsync(cached, req.RemoteEndPoint);
                    return;
                }

              
                var answer = await ForwardAsync(buf, req.RemoteEndPoint);
                if (answer != null)
                {
                    _cache[qname] = new CacheEntry
                    {
                        Response = (byte[])answer.Clone(),
                        ExpiresAt = DateTime.UtcNow.AddSeconds(60)
                    };
                    await SafeSendAsync(answer, req.RemoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke("处理 DNS 查询失败: " + ex.Message);
            }
        }

     
        private async Task SafeSendAsync(byte[] data, IPEndPoint remote)
        {
            try
            {
                if (_listener == null) return;
                await _listener.SendAsync(data, data.Length, remote);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset ||
                    ex.SocketErrorCode == SocketError.ConnectionRefused ||
                    ex.SocketErrorCode == SocketError.HostUnreachable ||
                    ex.SocketErrorCode == SocketError.NetworkUnreachable)
                {
                    return; 
                }
                OnError?.Invoke("发送 DNS 响应失败: " + ex.Message);
            }
            catch { }
        }

        private bool IsBlacklisted(string qname) => _matcher.IsBlocked(qname);

        private async Task<byte[]?> ForwardAsync(byte[] query, IPEndPoint client)
        {
            foreach (var upstream in Upstreams)
            {
                try
                {
                    using var udp = new UdpClient();
                   
                    try
                    {
                        udp.Client.IOControl(
                            (IOControlCode)SIO_UDP_CONNRESET,
                            new byte[] { 0, 0, 0, 0 },
                            null);
                    }
                    catch { }

                    udp.Client.ReceiveTimeout = 3000;
                    udp.Connect(upstream);
                    await udp.SendAsync(query, query.Length);

                    var task = udp.ReceiveAsync();
                    var done = await Task.WhenAny(task, Task.Delay(3000));
                    if (done != task) continue;

                    return task.Result.Buffer;
                }
                catch (SocketException)
                {
                   
                    continue;
                }
                catch { }
            }
            return null;
        }



        private static int GetQNameEnd(byte[] buf, int start)
        {
            int pos = start;
            while (pos < buf.Length)
            {
                int len = buf[pos];
                if (len == 0) return pos + 1;
                if ((len & 0xC0) == 0xC0) return pos + 2;
                pos += 1 + len;
            }
            return -1;
        }

        private static string ParseQName(byte[] buf, int start, out int endOffset)
        {
            var sb = new StringBuilder();
            int pos = start;
            endOffset = start;
            while (pos < buf.Length)
            {
                int len = buf[pos];
                if (len == 0) { pos++; endOffset = pos; break; }
                if ((len & 0xC0) == 0xC0) { pos += 2; endOffset = pos; break; }
                pos++;
                if (pos + len > buf.Length) return "";
                sb.Append(Encoding.ASCII.GetString(buf, pos, len));
                sb.Append('.');
                pos += len;
            }
            if (sb.Length > 0 && sb[^1] == '.') sb.Length--;
            return sb.ToString();
        }

        private static byte[] BuildBlockedResponse(byte[] query, int questionEnd)
        {
            var resp = new byte[questionEnd + 16];
            Array.Copy(query, 0, resp, 0, questionEnd);

            resp[2] = 0x81;
            resp[3] = 0x80;
            resp[6] = 0x00; resp[7] = 0x01;
            resp[8] = 0x00; resp[9] = 0x00;
            resp[10] = 0x00; resp[11] = 0x00;

            int p = questionEnd;
            resp[p++] = 0xC0; resp[p++] = 0x0C;
            resp[p++] = 0x00; resp[p++] = 0x01;
            resp[p++] = 0x00; resp[p++] = 0x01;
            resp[p++] = 0x00; resp[p++] = 0x00; resp[p++] = 0x00; resp[p++] = 0x3C;
            resp[p++] = 0x00; resp[p++] = 0x04;
            resp[p++] = 127; resp[p++] = 0; resp[p++] = 0; resp[p++] = 1;

            return resp;
        }

        public void Dispose() => Stop();
    }
}