// ==================== TunRouteManager.cs ====================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// TUN 网卡的地址配置与路由管理。
    ///
    /// ★ 安全设计：**不做 0.0.0.0/0 全网接管**。
    ///   只把"确实要拦截的 IP"以 /32 主机路由的方式送进 TUN，
    ///   其余流量完全不受影响 —— 因此即使本程序崩溃，也只是被拦的站点不可达，
    ///   不会让整台电脑断网。
    ///
    /// 路由一律用 store=active（不持久化），重启后自然清空；
    /// 另外启动时会主动清理上次残留的路由（自愈）。
    /// </summary>
    internal static class TunRouteManager
    {
        public const string AdapterName = "CFW-TUN";
        public const string AdapterType = "ClassFirewall";

        /// <summary>TUN 网卡自身的地址（RFC 2544 保留网段，不会与公网冲突）</summary>
        public const string GatewayAddress = "198.18.0.1";
        public const string AdapterMask = "255.255.255.0";

        /// <summary>路由器 MTU，避免分片</summary>
        public const int Mtu = 1420;

        private static readonly object Gate = new();
        private static readonly HashSet<string> AddedRoutes =
            new(StringComparer.OrdinalIgnoreCase);

        internal sealed class CommandResult
        {
            public int ExitCode;
            public string Output = "";
            public string Error = "";

            public bool Ok => ExitCode == 0;
            public string Detail =>
                string.IsNullOrWhiteSpace(Error) ? Output.Trim() : Error.Trim();
        }

        // ---------------- 网卡配置 ----------------

        /// <summary>给 TUN 网卡设置静态地址</summary>
        public static bool ConfigureAdapter(out string detail)
        {
            var r = RunNetsh(
                $"interface ipv4 set address name=\"{AdapterName}\" " +
                $"static {GatewayAddress} {AdapterMask}");

            // 地址已存在时 netsh 会返回非 0，这种情况可以忽略
            detail = r.Detail;
            if (r.Ok) return true;

            detail = r.Detail;
            return detail.Contains("已存在", StringComparison.OrdinalIgnoreCase) ||
                   detail.Contains("already exists", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>设置网卡 MTU</summary>
        public static bool SetMtu(out string detail)
        {
            var r = RunNetsh(
                $"interface ipv4 set subinterface \"{AdapterName}\" mtu={Mtu} store=active");
            detail = r.Detail;
            return r.Ok;
        }

        // ---------------- 路由 ----------------

        /// <summary>
        /// 把这些 IP 以 /32 路由送进 TUN。
        /// 返回成功加入的数量。
        /// </summary>
        public static int AddHostRoutes(IEnumerable<string> ips, Action<string>? log = null)
        {
            int ok = 0;
            var list = ips
                .Where(IsValidIpv4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var ip in list)
            {
                if (AddHostRoute(ip)) ok++;
            }

            if (list.Count > 0)
                log?.Invoke($"TUN 路由：{ok}/{list.Count} 个 IP 已导入 TUN 网卡");
            return ok;
        }

        public static bool AddHostRoute(string ip)
        {
            lock (Gate)
            {
                if (AddedRoutes.Contains(ip)) return true;

                // store=active：不写注册表，重启自动消失
                var r = RunNetsh(
                    $"interface ipv4 add route {ip}/32 \"{AdapterName}\" " +
                    $"{GatewayAddress} metric 1 store=active");

                if (r.Ok || r.Detail.Contains("已存在", StringComparison.OrdinalIgnoreCase) ||
                    r.Detail.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    AddedRoutes.Add(ip);
                    return true;
                }
                return false;
            }
        }

        public static void RemoveHostRoute(string ip)
        {
            lock (Gate)
            {
                RunNetsh($"interface ipv4 delete route {ip}/32 \"{AdapterName}\" {GatewayAddress}");
                AddedRoutes.Remove(ip);
            }
        }

        /// <summary>移除本次运行添加的所有路由</summary>
        public static void RemoveAllAddedRoutes(Action<string>? log = null)
        {
            List<string> snapshot;
            lock (Gate)
            {
                snapshot = AddedRoutes.ToList();
                AddedRoutes.Clear();
            }

            if (snapshot.Count == 0) return;

            foreach (var ip in snapshot)
                RunNetsh($"interface ipv4 delete route {ip}/32 \"{AdapterName}\" {GatewayAddress}");

            log?.Invoke($"TUN 路由：已移除 {snapshot.Count} 条主机路由");
        }

        /// <summary>把一条默认路由之外的兜底清理：删掉挂在 TUN 网卡上的所有路由</summary>
        public static void PurgeAllAdapterRoutes(Action<string>? log = null)
        {
            string ps =
                $"Get-NetRoute -InterfaceAlias '{AdapterName}' -ErrorAction SilentlyContinue " +
                $"| Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue";

            RunPowerShell(ps, log, silent: true);

            lock (Gate) AddedRoutes.Clear();
        }

        /// <summary>
        /// 启动时的自愈：清理上次异常退出可能残留的 TUN 路由，
        /// 保证不会因为上一次崩溃而留下黑洞路由。
        /// </summary>
        public static bool SelfHeal(Action<string>? log = null)
        {
            try
            {
                if (!AdapterExists())
                {
                    lock (Gate) AddedRoutes.Clear();
                    return false;
                }

                log?.Invoke("TUN：检测到上次运行残留的网卡/路由，正在清理...");
                PurgeAllAdapterRoutes(null);
                lock (Gate) AddedRoutes.Clear();
                log?.Invoke("TUN：残留路由已清理");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("TUN 自愈失败: " + ex.Message);
                return false;
            }
        }

        public static bool AdapterExists()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "interface ipv4 show interfaces")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return output.Contains(AdapterName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ---------------- 命令执行 ----------------

        internal static CommandResult RunNetsh(string args)
        {
            return Run("netsh", args);
        }

        internal static CommandResult Run(string file, string args)
        {
            var result = new CommandResult();
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    result.ExitCode = -1;
                    result.Error = "无法启动进程: " + file;
                    return result;
                }

                result.Output = p.StandardOutput.ReadToEnd();
                result.Error = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000))
                {
                    result.ExitCode = -1;
                    result.Error = "执行超时: " + file;
                    return result;
                }
                result.ExitCode = p.ExitCode;
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Error = ex.Message;
            }
            return result;
        }

        private static void RunPowerShell(string command, Action<string>? log, bool silent)
        {
            var r = Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                command.Replace("\"", "\\\"") + "\"");

            if (!r.Ok && !silent)
                log?.Invoke("TUN 清理命令失败: " + r.Detail);
        }

        internal static bool IsValidIpv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            return addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }
    }
}
