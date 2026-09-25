using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ClassFirewall
{
    public static class FirewallBlocker
    {
        public const string GroupName = "ClassFirewall";

        /// <summary>把 IP 列表同步到防火墙：先清空旧规则，再按当前 IP 添加</summary>
        public static void Sync(IEnumerable<string> ips, Action<string>? log = null)
        {
            ClearAll(log);

            var list = ips
                .Where(IsValidIPv4)
                .Distinct()
                .ToList();

            if (list.Count == 0)
            {
                log?.Invoke("防火墙规则已清空（无有效 IP）");
                return;
            }

            // 每个批次写入一条规则；每批 100 个 IP
            const int batchSize = 100;
            int ok = 0;
            int batchIndex = 0;

            for (int i = 0; i < list.Count; i += batchSize)
            {
                var batch = list.Skip(i).Take(batchSize).ToList();
                batchIndex++;
                var ruleName = $"CFW_{batchIndex}";

                // 构造 PowerShell 数组: @("1.2.3.4","5.6.7.8")
                var ipArray = "@(" +
                    string.Join(",", batch.Select(ip => $"\"{ip}\"")) +
                    ")";

                // PowerShell 命令（一行内完成）
                string psCommand =
                    $"New-NetFirewallRule " +
                    $"-DisplayName '{ruleName}' " +
                    $"-Group '{GroupName}' " +
                    $"-Direction Outbound " +
                    $"-Action Block " +
                    $"-RemoteAddress {ipArray} " +
                    $"-Profile Any " +
                    $"-Enabled True | Out-Null";

                if (RunPowerShell(psCommand, log))
                    ok += batch.Count;
            }

            log?.Invoke($"✅ 防火墙已阻断 {ok} 个 IP");
        }

        public static void ClearAll(Action<string>? log = null)
        {
            string psCommand =
                $"Get-NetFirewallRule -Group '{GroupName}' -ErrorAction SilentlyContinue " +
                $"| Remove-NetFirewallRule -ErrorAction SilentlyContinue";
            _ = RunPowerShell(psCommand, log, silent: true);
        }

        private static bool RunPowerShell(string command, Action<string>? log, bool silent = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                                command.Replace("\"", "\\\"") + "\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                _ = p.WaitForExit(15000);

                if (p.ExitCode != 0 && !silent)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    log?.Invoke($"⚠ PowerShell 失败({p.ExitCode}): {Truncate(detail, 300)}");
                    return false;
                }
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                if (!silent) log?.Invoke($"⚠ 调用 PowerShell 异常: {ex.Message}");
                return false;
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static bool IsValidIPv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            var parts = ip.Split('.');
            if (parts.Length != 4) return false;
            foreach (var s in parts)
                if (!byte.TryParse(s, out _)) return false;
            return true;
        }
    }
}