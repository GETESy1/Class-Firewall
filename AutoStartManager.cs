using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ClassFirewall
{
    /// <summary>
    /// 开机自启：用**任务计划程序**建一个「登录时触发、以最高权限运行」的任务。
    /// 这是 Windows 上唯一能**免 UAC 静默提权**的官方途径 —— 启动项做不到，
    /// 因为启动项只能以普通用户权限拉起程序，而本程序必须提权
    /// （改网卡 DNS、写浏览器策略、结束占用 53 端口的进程）。
    ///
    /// ★ 为什么用 /XML 而不是命令行参数：
    ///   <c>/TR "…"</c> 在「路径含空格」时要再套一层引号，而 schtasks 对多重引号的解析
    ///   很不可靠 —— exe 只要放在带空格的目录（<c>C:\Program Files\…</c>，或本项目的
    ///   "Class Firewall"）就会报 <c>Invalid argument/option</c> 或 <c>系统找不到指定的路径</c>。
    ///   XML 里 &lt;Command&gt; 与 &lt;Arguments&gt; 分开写，完全没有引号问题。
    ///
    /// ★ 同时修掉命令行方式建任务会中的三个默认值坑：
    ///   · ExecutionTimeLimit 默认 72 小时 → 到点 Windows 把进程**杀掉**，屏蔽消失
    ///   · DisallowStartIfOnBatteries 默认 true → 笔记本用电池时**根本不启动**
    ///   · StopIfGoingOnBatteries 默认 true → 一拔电源就被停掉
    ///
    /// ★ 失败时带回 schtasks 的原始输出，界面上能看到具体原因。
    /// </summary>
    public static class AutoStartManager
    {
        internal const string TaskName = "ClassFirewallAutoStart";

        /// <summary>之前用「启动项」方案写过的位置，升级时清掉，避免两套自启同时生效</summary>
        internal const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        internal const string RunValueName = "ClassFirewall";

        // ---------------- 查询 / 启用 / 停用 ----------------

        public static bool IsEnabled() => IsEnabled(out _);

        public static bool IsEnabled(out string detail)
        {
            var (code, output) = RunSchTasks($"/Query /TN \"{TaskName}\"");
            detail = code == 0 ? "计划任务已存在" : Trim(output);
            return code == 0;
        }

        /// <summary>创建（或覆盖）自启任务；失败时 detail 带 schtasks 原始输出</summary>
        public static bool Enable(out string detail)
        {
            string exe = CurrentExePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                detail = "取不到当前 exe 路径";
                return false;
            }

            string legacy = RemoveLegacyRunEntries();

            // 不先删再建：schtasks 的 /F 本身就是覆盖式注册，是原子操作。
            // 先删会留一个"任务已删、新任务还没建"的空窗，万一此刻断电/崩溃就没了。

            if (CreateFromXml(exe, out string xmlDetail))
            {
                detail = "已用 XML 方式创建计划任务（最高权限运行，免 UAC）" + legacy;
                return true;
            }

            // XML 失败时退回命令行方式。
            // 注意：这种方式在「exe 路径含空格」时**必定失败**
            //（schtasks 会把参数从空格处切断，报"无效参数/选项"），
            // 本项目的目录名就带空格，所以它只是个理论上的兜底。
            if (CreateFromCommandLine(exe, out string cmdDetail))
            {
                detail = "XML 方式失败（" + Trim(xmlDetail) + "），已改用命令行方式创建" + legacy;
                return true;
            }

            detail = "XML 方式：" + Trim(xmlDetail) + Environment.NewLine +
                     "命令行方式（路径含空格时必然失败）：" + Trim(cmdDetail);
            return false;
        }

        public static bool Disable() => Disable(out _);

        public static bool Disable(out string detail)
        {
            var (_, output) = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
            detail = Trim(output);
            return true;
        }

        // ---------------- 清理旧方案留下的启动项 ----------------

        /// <summary>删除「启动项」方案写过的 Run 键值；返回一句说明，没有则返回空串</summary>
        public static string RemoveLegacyRunEntries()
        {
            bool removed = false;

            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(RunKeyPath, true);
                    if (key?.GetValue(RunValueName) == null) continue;

                    key.DeleteValue(RunValueName, false);
                    removed = true;
                }
                catch { /* 删不掉就算了，不影响主流程 */ }
            }

            return removed ? "；并清除了旧的启动项" : "";
        }

        // ---------------- 两种创建方式 ----------------

        private static bool CreateFromXml(string exe, out string detail)
        {
            string xmlPath = Path.Combine(Path.GetTempPath(),
                "ClassFirewall-autostart-" + Guid.NewGuid().ToString("N") + ".xml");

            try
            {
                // schtasks 要求 XML 为 UTF-16
                File.WriteAllText(xmlPath, BuildTaskXml(exe), new UnicodeEncoding(false, true));

                var (code, output) = RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
                detail = output;
                return code == 0;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
            }
        }

        private static bool CreateFromCommandLine(string exe, out string detail)
        {
            var (code, output) = RunSchTasks(
                $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" -silent\" " +
                "/SC ONLOGON /RL HIGHEST /F");
            detail = output;
            return code == 0;
        }

        /// <summary>任务定义。抽出来便于单测（校验 XML 合法性与路径转义）</summary>
        internal static string BuildTaskXml(string exe, string? userName = null)
        {
            // ★ 刻意不写 <UserId>：
            //   写 UserId 会引入一类已知的注册失败
            //   （System.ArgumentException: (nn,nn):UserId:xxx），
            //   在机器名偏长 / 工作组环境 / 账户名解析异常时尤其容易中招。
            //   省略 UserId 时，任务计划程序默认就用「注册它的那个用户」，
            //   语义与我们要的完全一致，还少一个失败点。
            //   "以最高权限运行、免 UAC" 由 RunLevel=HighestAvailable 负责。
            _ = userName;

            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Class Firewall 开机自启（最高权限运行，免 UAC）</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{Escape(exe)}</Command>
      <Arguments>-silent</Arguments>
    </Exec>
  </Actions>
</Task>
";
        }

        private static string Escape(string s) =>
            System.Security.SecurityElement.Escape(s) ?? s;

        private static string CurrentExePath() =>
            Process.GetCurrentProcess().MainModule?.FileName ?? "";

        private static string Trim(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 240 ? s : s.Substring(0, 240) + "...";
        }

        private static (int code, string output) RunSchTasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var p = Process.Start(psi);
                if (p == null) return (-1, "无法启动 schtasks");

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                if (!p.WaitForExit(20000))
                    return (-1, "schtasks 执行超时（20 秒）");

                return (p.ExitCode, (stdout + " " + stderr).Trim());
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }
}
