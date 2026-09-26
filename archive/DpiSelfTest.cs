// ==================== DpiSelfTest.cs ====================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ClassFirewall
{
    /// <summary>
    /// DPI 链路自检。
    ///
    /// 「DNS 能拦住但看不到 403 页面」可能卡在好几个不同的环节上：
    ///   根证书没装 / 监听没起来 / MITM 握手失败 / 黑名单是空的
    /// 这个自检逐环验证，直接指出断在哪一步，不用靠猜。
    ///
    /// 第 4 步是真的发一次 HTTPS 请求走完整条链路，
    /// 因此能验证 MITM 的 ClientHello 回放是否真的生效。
    /// </summary>
    internal static class DpiSelfTest
    {
        internal sealed class Result
        {
            public readonly List<string> Lines = new();
            public bool AllPassed = true;
            public bool HttpOk;
            public bool HttpsOk;

            public void Pass(string msg) => Lines.Add("  ✔ " + msg);
            public void Fail(string msg) { Lines.Add("  ✖ " + msg); AllPassed = false; }
            public void Info(string msg) => Lines.Add("  · " + msg);
        }

        public static async Task<Result> RunAsync(
            int httpPort, int httpsPort, IReadOnlyList<string> sampleDomains)
        {
            var r = new Result();

            // ---- 1) 根证书 ----
            // 只影响 HTTPS 那一环，绝不能因此中断后面的检查 ——
            // 「没装证书」恰恰是最需要看到其余环节状态的场景。
            bool certInstalled = CertificateAuthority.IsInstalled();
            if (certInstalled)
                r.Pass("根证书已安装到系统信任区 —— HTTPS 命中可以显示拦截页");
            else
                r.Fail("根证书未安装 —— HTTPS 命中只会断连（ERR_CONNECTION_RESET），" +
                       "这是「看不到 403」最常见的原因。请点「安装根证书」");

            // ---- 2) 黑名单 ----
            if (sampleDomains.Count == 0)
            {
                r.Fail("黑名单是空的：没有任何勾选的网站，DPI 自然什么都不会拦");
                return r;
            }

            string domain = sampleDomains[0];
            r.Info($"用测试域名：{domain}（来自当前勾选的网站）");

            // ---- 3) 监听是否在 ----
            bool httpUp = await ProbeTcpAsync(httpPort);
            bool httpsUp = await ProbeTcpAsync(httpsPort);

            if (httpUp) r.Pass($"HTTP {httpPort} 端口有人监听");
            else FailPort(r, httpPort, "HTTP");

            if (httpsUp) r.Pass($"HTTPS {httpsPort} 端口有人监听");
            else FailPort(r, httpsPort, "HTTPS");

            if (!httpUp && !httpsUp)
            {
                r.Info("两个端口都没人监听，跳过实际请求测试 —— 请先勾选「启用深度包检查」");
                return r;
            }

            // ---- 4) 真发 HTTP 请求 ----
            if (httpUp)
            {
                var (status, note) = await TryHttpAsync(httpPort, domain);
                if (status == 403)
                {
                    r.HttpOk = true;
                    r.Pass($"HTTP 拦截生效：127.0.0.1:{httpPort} 返回 403");
                }
                else
                {
                    r.Fail($"HTTP 拦截未生效（{Describe(status)}）：{note}");
                }
            }

            // ---- 5) 真发 HTTPS 请求（验证 MITM 的 ClientHello 回放）----
            if (httpsUp)
            {
                var (status, note) = await TryHttpsAsync(httpsPort, domain);
                if (status == 403)
                {
                    r.HttpsOk = true;
                    r.Pass("HTTPS 拦截生效：MITM 握手成功并返回 403");
                }
                else if (!certInstalled)
                {
                    r.Info($"HTTPS 未返回 403（{Describe(status)}）" +
                           "—— 与「根证书未安装」一致，装了证书后再自检一次");
                }
                else
                {
                    r.Fail($"HTTPS 拦截未生效（{Describe(status)}）：{note}");
                }
            }

            return r;
        }

        private static string Describe(int status) =>
            status == 0 ? "无响应" : "HTTP " + status;

        private static bool FailPort(Result r, int port, string name)
        {
            r.Fail($"{name} {port} 端口没有监听 —— 服务没起来，或被别的程序占用了");
            return false;
        }

        private static async Task<bool> ProbeTcpAsync(int port)
        {
            try
            {
                using var c = new TcpClient();
                var task = c.ConnectAsync("127.0.0.1", port);
                var done = await Task.WhenAny(task, Task.Delay(1500));
                return done == task && c.Connected;
            }
            catch { return false; }
        }

        /// <summary>明文 HTTP：发 Host 头，看是否返回 403</summary>
        private static async Task<(int status, string note)> TryHttpAsync(int port, string domain)
        {
            try
            {
                using var c = new TcpClient();
                var connect = c.ConnectAsync("127.0.0.1", port);
                if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
                    return (0, "连接超时");

                c.ReceiveTimeout = 5000;
                c.SendTimeout = 5000;
                using var s = c.GetStream();

                var req = Encoding.ASCII.GetBytes(
                    $"GET / HTTP/1.1\r\nHost: {domain}\r\nUser-Agent: ClassFirewall-SelfTest\r\n" +
                    "Connection: close\r\n\r\n");
                await s.WriteAsync(req);

                string head = await ReadHeadAsync(s, 5000);
                return (ParseStatus(head),
                    head.Length == 0 ? "没有收到任何响应（连接被直接关闭）" : FirstLine(head));
            }
            catch (Exception ex)
            {
                return (0, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>HTTPS：完成真实 TLS 握手后再发请求，验证 MITM 链路</summary>
        private static async Task<(int status, string note)> TryHttpsAsync(int port, string domain)
        {
            try
            {
                using var c = new TcpClient();
                var connect = c.ConnectAsync("127.0.0.1", port);
                if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
                    return (0, "连接超时");

                c.ReceiveTimeout = 8000;
                c.SendTimeout = 8000;

                // 自检只关心链路是否通，不校验证书本身（用回调全部接受）
                using var ssl = new SslStream(c.GetStream(), false, (_, _, _, _) => true);
                var auth = ssl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = domain });

                if (await Task.WhenAny(auth, Task.Delay(8000)) != auth)
                    return (0, "TLS 握手超时 —— MITM 很可能没有把 ClientHello 回放给 SslStream");
                await auth;   // 抛出真实的握手异常

                var req = Encoding.ASCII.GetBytes(
                    $"GET / HTTP/1.1\r\nHost: {domain}\r\nUser-Agent: ClassFirewall-SelfTest\r\n" +
                    "Connection: close\r\n\r\n");
                await ssl.WriteAsync(req);
                await ssl.FlushAsync();

                string head = await ReadHeadAsync(ssl, 5000);
                return (ParseStatus(head),
                    head.Length == 0 ? "握手成功但没有收到响应" : FirstLine(head));
            }
            catch (Exception ex)
            {
                return (0, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static async Task<string> ReadHeadAsync(Stream s, int timeoutMs)
        {
            var buf = new byte[1024];
            var read = s.ReadAsync(buf, 0, buf.Length);
            if (await Task.WhenAny(read, Task.Delay(timeoutMs)) != read)
                return "";

            int n = await read;

            // 先读一小段，超时或读满都返回，够判断状态行了
            return n > 0 ? Encoding.ASCII.GetString(buf, 0, n) : "";
        }

        private static string FirstLine(string head)
        {
            if (string.IsNullOrEmpty(head)) return "";
            var idx = head.IndexOf('\n');
            return (idx < 0 ? head : head.Substring(0, idx)).Trim();
        }

        private static int ParseStatus(string head)
        {
            if (string.IsNullOrEmpty(head)) return 0;
            var line = head.Split('\n')[0].Trim();

            // 形如 HTTP/1.1 403 Forbidden
            var parts = line.Split(' ');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int code)) return code;
            return 0;
        }
    }
}
