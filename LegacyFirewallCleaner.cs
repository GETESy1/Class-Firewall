// ==================== LegacyFirewallCleaner.cs ====================
using System;
using System.Diagnostics;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// 旧版本遗留的 Windows 防火墙规则清理器。
    ///
    /// ★ 现在的 IP 层拦截由 TUN 的 /32 路由 + TCP RST 负责，
    ///   不再往系统写防火墙出站规则（旧的 FirewallBlocker 已移除）。
    ///   但旧版本写进系统的是**持久化**规则，进程退出后依然生效，
    ///   如果没人清理，用户就会一直卡着一堆拆不掉的拦截规则。
    ///   这里负责在启动/解除时把 ClassFirewall 的规则摘干净。
    ///
    /// 只删除本程序自己创建的规则，不影响用户和其他软件的防火墙配置。
    /// 整个过程只用一次 PowerShell 调用，避免拖慢启动。
    /// </summary>
    internal static class LegacyFirewallCleaner
    {
        public const string GroupName = "ClassFirewall";

        /// <summary>旧版本 FirewallBlocker 写的规则名前缀：CFW_1、CFW_2 ...</summary>
        public const string RuleNamePrefix = "CFW_";

        public const int Failed = -1;

        /// <summary>
        /// 移除本程序创建过的全部防火墙规则，返回删除的规则条数。
        /// 本来就没有规则时返回 0；执行失败返回 <see cref="Failed"/>。
        /// </summary>
        public static int Purge(out string detail)
        {
            // 按"规则组"和"规则名前缀"两种方式找，兼容历史版本没写组名的情况；
            // 用 Name 去重，避免同一条规则被算两次。最后一行输出条数供解析。
            string ps =
                "$a = @(Get-NetFirewallRule -Group '" + GroupName + "' -ErrorAction SilentlyContinue) + " +
                "@(Get-NetFirewallRule -DisplayName '" + RuleNamePrefix + "*' -ErrorAction SilentlyContinue); " +
                "$u = @($a | Sort-Object -Property Name -Unique); " +
                "if ($u.Count -gt 0) { $u | Remove-NetFirewallRule -ErrorAction SilentlyContinue }; " +
                "Write-Output $u.Count";

            detail = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"",
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
                    detail = "无法启动 powershell.exe";
                    return Failed;
                }

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(20000))
                {
                    detail = "执行超时";
                    return Failed;
                }

                detail = (stdout + " " + stderr).Trim();

                if (p.ExitCode != 0)
                    return Failed;

                // 取输出里最后一个可解析为整数的行（PowerShell 可能夹带警告文本）
                foreach (var line in stdout.Split('\n'))
                {
                    var t = line.Trim();
                    if (int.TryParse(t, out int count)) return count;
                }

                return 0;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return Failed;
            }
        }
    }
}
