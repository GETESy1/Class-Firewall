// ==================== TunEngine.cs ====================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClassFirewall
{
    /// <summary>
    /// TUN 模式引擎：接管"被拦截目标"的流量，在用户态完成拦截。
    ///
    /// 职责边界（诚实声明）：
    ///   ✅ DNS 拦截：客户端即使硬编码用公共 DNS（8.8.8.8 / 114.114.114.114 ...），
    ///      命中黑名单的域名也会被解析到 127.0.0.1
    ///   ✅ DNS 转发：未命中的查询转发到上游并回写，保证正常上网不受影响
    ///   ✅ IP 兜底拦截：对写进 TUN 的拦截 IP，直接回 TCP RST（快速失败）
    ///   ❌ 不做 0.0.0.0/0 全网接管，也不做 TCP 转发：
    ///      完整转发需要用户态 TCP 栈（tun2socks 之类），未集成。
    ///      因此 TUN 不解析 SNI —— SNI/Host 拦截仍由 DPI 层负责。
    /// </summary>
    internal sealed class TunEngine : IDisposable
    {
        /// <summary>
        /// 会被 TUN 拦截的公共 DNS 服务器。
        ///
        /// ★ 刻意排除 223.5.5.5 / 223.6.6.6：
        ///   这两个是本程序转发 DNS 时使用的上游，如果把上游也路由进 TUN，
        ///   我们自己的转发查询会绕回 TUN 形成死循环。
        /// </summary>
        private static readonly string[] PublicDnsIps =
        {
            "8.8.8.8", "8.8.4.4",            // Google
            "1.1.1.1", "1.0.0.1",            // Cloudflare
            "9.9.9.9",                        // Quad9
            "208.67.222.222", "208.67.220.220", // OpenDNS
            "114.114.114.114", "114.114.115.115",
            "119.29.29.29", "182.254.116.116", // DNSPod
            "180.76.76.76",                    // Baidu
            "1.2.4.8", "210.2.4.8",            // 全国 DNS
            "101.226.4.6", "218.30.118.6"      // 电信
        };

        /// <summary>转发 DNS 查询时使用的上游（必须不在上面那张表里）</summary>
        private static readonly IPEndPoint[] RelayUpstreams =
        {
            new IPEndPoint(IPAddress.Parse("223.5.5.5"), 53),
            new IPEndPoint(IPAddress.Parse("223.6.6.6"), 53)
        };

        /// <summary>命中黑名单时返回的地址：交给本机 DPI 层显示拦截页 / 断连</summary>
        private static readonly uint BlackholeAddress = PacketCodec.ParseIpv4Address("127.0.0.1");

        private readonly DomainMatcher _matcher = new();

        private readonly ConcurrentDictionary<string, byte> _blockedIps =
            new(StringComparer.OrdinalIgnoreCase);

        private IntPtr _adapter = IntPtr.Zero;
        private IntPtr _session = IntPtr.Zero;
        private IntPtr _readEvent = IntPtr.Zero;

        private CancellationTokenSource? _cts;
        private Thread? _pump;
        private readonly object _sendLock = new();

        public bool IsRunning { get; private set; }

        // ---- 统计（供 UI 状态栏）----
        public long PacketsIn;
        public long DnsBlocked;
        public long DnsRelayed;
        public long TcpResetSent;
        public long PacketsDropped;

        public event Action<string>? OnBlocked;
        public event Action<string>? OnError;
        public event Action<string>? OnDebug;

        // ---------------- 黑名单 ----------------

        public void UpdateBlacklist(IEnumerable<string> domains) => _matcher.Update(domains);

        /// <summary>更新要送进 TUN 的拦截 IP（由外部 DNS 解析得到）</summary>
        public void UpdateBlockedIps(IEnumerable<string> ips, Action<string>? log = null)
        {
            _blockedIps.Clear();
            var list = new List<string>();

            foreach (var ip in ips)
            {
                if (!TunRouteManager.IsValidIpv4(ip)) continue;
                if (_blockedIps.TryAdd(ip, 1)) list.Add(ip);
            }

            if (IsRunning && list.Count > 0)
                TunRouteManager.AddHostRoutes(list, log);
        }

        public static IReadOnlyList<string> InterceptedDnsServers => PublicDnsIps;

        // ---------------- 启停 ----------------

        public bool Start(out string error)
        {
            error = "";
            if (IsRunning)
                return true;

            if (!WintunLoader.TryInitialize())
            {
                error = WintunLoader.LastError;
                return false;
            }

            // 先清理上次可能残留的路由（崩溃自愈）
            TunRouteManager.SelfHeal(msg => OnDebug?.Invoke(msg));

            try
            {
                _adapter = OpenOrCreateAdapter(out error);
                if (_adapter == IntPtr.Zero) return false;

                _session = WintunInterop.WintunStartSession(
                    _adapter, WintunInterop.DefaultRingCapacity);
                if (_session == IntPtr.Zero)
                {
                    error = $"启动 TUN 会话失败（Win32 错误 {Marshal.GetLastWin32Error()}）";
                    CleanupNative();
                    return false;
                }

                _readEvent = WintunInterop.WintunGetReadWaitEvent(_session);
                if (_readEvent == IntPtr.Zero)
                    OnDebug?.Invoke("TUN：未能取到读取等待事件，将改用轮询");

                // 网卡地址与 MTU
                if (!TunRouteManager.ConfigureAdapter(out string detail))
                    OnDebug?.Invoke("TUN：设置网卡地址返回: " + detail);
                TunRouteManager.SetMtu(out _);

                // 把公共 DNS 送进 TUN，用于拦截"自带 DNS"的应用
                TunRouteManager.AddHostRoutes(PublicDnsIps, msg => OnDebug?.Invoke(msg));

                // 已有的拦截 IP 一并导入
                if (_blockedIps.Count > 0)
                    TunRouteManager.AddHostRoutes(_blockedIps.Keys.ToList(), msg => OnDebug?.Invoke(msg));

                _cts = new CancellationTokenSource();
                IsRunning = true;

                _pump = new Thread(() => PumpLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "ClassFirewall-TUN"
                };
                _pump.Start();

                return true;
            }
            catch (Exception ex)
            {
                error = $"启动 TUN 失败: {ex.GetType().Name}: {ex.Message}";
                Stop();
                return false;
            }
        }

        private IntPtr OpenOrCreateAdapter(out string error)
        {
            error = "";

            IntPtr adapter = WintunInterop.WintunCreateAdapter(
                TunRouteManager.AdapterName, TunRouteManager.AdapterType, IntPtr.Zero);

            if (adapter != IntPtr.Zero) return adapter;

            int err = Marshal.GetLastWin32Error();

            // 183 = ERROR_ALREADY_EXISTS：上次运行的网卡还在，直接打开复用
            if (err == 183)
            {
                adapter = WintunInterop.WintunOpenAdapter(TunRouteManager.AdapterName);
                if (adapter != IntPtr.Zero)
                {
                    OnDebug?.Invoke("TUN：复用已存在的 " + TunRouteManager.AdapterName + " 网卡");
                    return adapter;
                }
            }

            error = err == 5
                ? "创建 TUN 网卡被拒绝（错误 5）：请以管理员身份运行本程序。"
                : $"创建 TUN 网卡失败（Win32 错误 {err}）。";
            return IntPtr.Zero;
        }

        public void Stop()
        {
            if (!IsRunning && _session == IntPtr.Zero && _adapter == IntPtr.Zero)
                return;

            IsRunning = false;

            try { _cts?.Cancel(); } catch { }

            try
            {
                if (_pump != null && _pump.IsAlive)
                    _pump.Join(TimeSpan.FromSeconds(3));
            }
            catch { }
            _pump = null;

            // ★ 关键：先把我们加的路由撤掉，再关闭网卡，确保不留黑洞路由
            try { TunRouteManager.RemoveAllAddedRoutes(msg => OnDebug?.Invoke(msg)); } catch { }

            CleanupNative();

            // 兜底：把挂在该网卡上的残留路由全部清掉
            try { TunRouteManager.PurgeAllAdapterRoutes(msg => OnDebug?.Invoke(msg)); } catch { }

            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        private void CleanupNative()
        {
            if (_session != IntPtr.Zero)
            {
                try { WintunInterop.WintunEndSession(_session); } catch { }
                _session = IntPtr.Zero;
            }
            if (_adapter != IntPtr.Zero)
            {
                try { WintunInterop.WintunCloseAdapter(_adapter); } catch { }
                _adapter = IntPtr.Zero;
            }
            _readEvent = IntPtr.Zero;
        }

        // ---------------- 数据面 ----------------

        private void PumpLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    IntPtr packet = WintunInterop.WintunReceivePacket(_session, out uint size);

                    if (packet != IntPtr.Zero)
                    {
                        try
                        {
                            var buf = new byte[size];
                            Marshal.Copy(packet, buf, 0, (int)size);
                            Interlocked.Increment(ref PacketsIn);
                            try { HandlePacket(buf); }
                            catch (Exception ex) { OnError?.Invoke("TUN 处理报文异常: " + ex.Message); }
                        }
                        finally
                        {
                            WintunInterop.WintunReleaseReceivePacket(_session, packet);
                        }
                        continue;   // 队列里可能还有，继续取
                    }

                    // 队列空：等一下可读事件（没有事件就退化成轮询）
                    if (_readEvent != IntPtr.Zero)
                        WintunInterop.WaitForSingleObject(_readEvent, WintunInterop.ReadWaitTimeoutMs);
                    else
                        Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    OnError?.Invoke("TUN 收包循环终止: " + ex.Message);
            }
        }

        private void HandlePacket(byte[] buf)
        {
            if (!PacketCodec.TryParseIpv4(buf, out var ip))
            {
                Interlocked.Increment(ref PacketsDropped);
                return;
            }

            switch (ip.Protocol)
            {
                case PacketCodec.ProtocolUdp:
                    if (PacketCodec.TryParseUdp(buf, ip, out var udp) && udp.DestinationPort == 53)
                        HandleDns(buf, ip, udp);
                    else
                        Interlocked.Increment(ref PacketsDropped);
                    break;

                case PacketCodec.ProtocolTcp:
                    if (PacketCodec.TryParseTcp(buf, ip, out var tcp))
                        HandleTcp(buf, ip, tcp);
                    else
                        Interlocked.Increment(ref PacketsDropped);
                    break;

                default:
                    Interlocked.Increment(ref PacketsDropped);
                    break;
            }
        }

        // ---------------- DNS ----------------

        private void HandleDns(byte[] buf, PacketCodec.Ipv4Packet ip, PacketCodec.UdpDatagram udp)
        {
            if (!PacketCodec.TryParseDnsQuestion(buf, udp.PayloadOffset, udp.PayloadLength,
                    out string qname, out int questionEnd))
            {
                Interlocked.Increment(ref PacketsDropped);
                return;
            }

            if (IsBlocked(qname))
            {
                var answer = PacketCodec.BuildDnsAResponse(
                    buf, udp.PayloadOffset, udp.PayloadLength, questionEnd, BlackholeAddress);

                SendPacket(PacketCodec.BuildDnsReplyPacket(ip, udp, answer));

                Interlocked.Increment(ref DnsBlocked);
                OnBlocked?.Invoke(qname);
                return;
            }

            // 未命中：转发到上游并把结果原样送回
            var payload = new byte[udp.PayloadLength];
            Array.Copy(buf, udp.PayloadOffset, payload, 0, udp.PayloadLength);

            uint clientAddr = ip.SourceAddress;
            int clientPort = udp.SourcePort;
            uint serverAddr = ip.DestinationAddress;
            int serverPort = udp.DestinationPort;

            _ = Task.Run(() => RelayDnsAsync(payload, clientAddr, clientPort, serverAddr, serverPort));
        }

        private async Task RelayDnsAsync(byte[] payload,
            uint clientAddr, int clientPort, uint serverAddr, int serverPort)
        {
            foreach (var upstream in RelayUpstreams)
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 3000;
                    udp.Connect(upstream);
                    await udp.SendAsync(payload, payload.Length);

                    var task = udp.ReceiveAsync();
                    var done = await Task.WhenAny(task, Task.Delay(3000)).ConfigureAwait(false);
                    if (done != task) continue;

                    var response = task.Result.Buffer;
                    if (response.Length == 0) continue;

                    // 用原查询的地址/端口反向包装，送回客户端
                    var reply = PacketCodec.BuildUdp(
                        serverAddr, clientAddr, serverPort, clientPort, response);

                    SendPacket(reply);
                    Interlocked.Increment(ref DnsRelayed);
                    return;
                }
                catch (SocketException) { /* 换下一个上游 */ }
                catch { }
            }

            Interlocked.Increment(ref PacketsDropped);
        }

        // ---------------- TCP ----------------

        private void HandleTcp(byte[] buf, PacketCodec.Ipv4Packet ip, PacketCodec.TcpSegment tcp)
        {
            string dst = PacketCodec.FormatIpv4Address(ip.DestinationAddress);

            // 只有被路由进 TUN 的拦截 IP 才会走到这里
            if (!_blockedIps.ContainsKey(dst))
            {
                Interlocked.Increment(ref PacketsDropped);
                return;
            }

            // 直接回 RST：让应用立刻得到"连接被重置"，而不是干等超时
            var reset = PacketCodec.BuildTcpReset(buf, ip, tcp);
            if (SendPacket(reset))
            {
                Interlocked.Increment(ref TcpResetSent);
                OnBlocked?.Invoke($"{dst}:{tcp.DestinationPort} (TCP RST)");
            }
        }

        // ---------------- 发送 ----------------

        private bool SendPacket(byte[] packet)
        {
            if (_session == IntPtr.Zero || packet.Length == 0) return false;

            lock (_sendLock)
            {
                IntPtr p = WintunInterop.WintunAllocateSendPacket(_session, (uint)packet.Length);
                if (p == IntPtr.Zero) return false;

                try
                {
                    Marshal.Copy(packet, 0, p, packet.Length);
                    WintunInterop.WintunSendPacket(_session, p);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        // ---------------- 黑名单匹配 ----------------

        internal bool IsBlocked(string domain) => _matcher.IsBlocked(domain);

        public string StatusText =>
            $"TUN 收包 {PacketsIn}，DNS 拦截 {DnsBlocked}，DNS 转发 {DnsRelayed}，" +
            $"RST {TcpResetSent}，丢弃 {PacketsDropped}";

        public void Dispose() => Stop();
    }
}
