using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ClassFirewall
{
    /// <summary>
    /// 查找并结束占用指定端口的进程（保护系统关键进程）。
    /// </summary>
    public static class PortHelper
    {
        public sealed class PortProcess
        {
            public int Pid { get; set; }
            public string Name { get; set; } = "";
            public bool IsCritical { get; set; }
        }

        /// <summary>找出占用指定端口的进程</summary>
        public static List<PortProcess> FindProcessesUsingPort(int port)
        {
            var result = new List<PortProcess>();
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                if (p == null) return result;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);

                var seenPids = new HashSet<int>();
                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    if (!parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) &&
                        !parts[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // parts[1] 形如 0.0.0.0:443 或 [::]:443
                    string localAddr = parts[1];
                    int colon = localAddr.LastIndexOf(':');
                    if (colon < 0) continue;
                    if (!int.TryParse(localAddr.Substring(colon + 1), out int p1)) continue;
                    if (p1 != port) continue;

                    // 最后一段是 PID
                    if (!int.TryParse(parts[parts.Length - 1], out int pid)) continue;
                    if (pid <= 0 || seenPids.Contains(pid)) continue;
                    seenPids.Add(pid);

                    string name;
                    try { name = Process.GetProcessById(pid).ProcessName; }
                    catch { name = "(已退出)"; }

                    result.Add(new PortProcess
                    {
                        Pid = pid,
                        Name = name,
                        IsCritical = IsCriticalProcess(name)
                    });
                }
            }
            catch { /* 忽略 */ }
            return result;
        }

        public static bool IsCriticalProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".exe")) lower = lower[..^4];

            var critical = new[]
            {
                "system", "idle", "csrss", "smss", "wininit", "services",
                "lsass", "winlogon", "svchost", "dwm", "explorer", "spoolsv",
                "taskhostw", "sihost", "fontdrvhost", "registry", "memory compression"
            };
            foreach (var c in critical)
                if (lower == c) return true;
            return false;
        }

        /// <summary>
        /// 尝试结束占用端口的进程。
        /// 返回 (成功结束列表, 跳过/失败列表)。
        /// </summary>
        public static (List<string> killed, List<string> skipped) ReleasePort(int port)
        {
            var killed = new List<string>();
            var skipped = new List<string>();

            foreach (var p in FindProcessesUsingPort(port))
            {
                if (p.IsCritical)
                {
                    skipped.Add($"{p.Name}(PID:{p.Pid})[系统进程]");
                    continue;
                }
                try
                {
                    var proc = Process.GetProcessById(p.Pid);
                    proc.Kill();
                    proc.WaitForExit(3000);
                    killed.Add($"{p.Name}(PID:{p.Pid})");
                }
                catch (Exception ex)
                {
                    skipped.Add($"{p.Name}(PID:{p.Pid})[{ex.Message}]");
                }
            }
            return (killed, skipped);
        }
    }
}