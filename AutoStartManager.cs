using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace ClassFirewall
{
    /// <summary>
    /// 开机自启：用任务计划程序建一个「登录时触发、最高权限运行」的任务。
    /// 这是 Windows 上唯一能免 UAC 静默提权的途径；启动项（Run 键）做不到，
    /// 而且在本机还会被火绒 HIPS 拦住写入。
    /// </summary>
    public static class AutoStartManager
    {
        internal const string TaskName = "ClassFirewallAutoStart";
        public static bool IsEnabled() => IsEnabled(out _);

        public static bool IsEnabled(out string detail)
        {
            var (code, output) = RunSchTasks($"/Query /TN \"{TaskName}\"");
            detail = code == 0 ? "任务已存在" : Trim(output);
            return code == 0;
        }

        public static bool Enable(out string detail)
        {
            string exe = CurrentExePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                detail = "取不到当前 exe 路径";
                return false;
            }


            // /XML 方式：Command 与 Arguments 分开写，不存在引号转义问题。
            // 命令行 /TR 方式在「路径含空格」时必定失败（schtasks 会从空格处切断参数）。
            if (CreateFromXml(exe, out string xmlDetail))
            {
                detail = "XML 方式" ;
                return true;
            }

            if (CreateFromCommandLine(exe, out string cmdDetail))
            {
                detail = "命令行方式（XML 失败：" + Trim(xmlDetail) + "）" ;
                return true;
            }

            detail = "XML：" + Trim(xmlDetail) + Environment.NewLine + "命令行：" + Trim(cmdDetail);
            return false;
        }

        public static bool Disable() => Disable(out _);

        public static bool Disable(out string detail)
        {
            var (_, output) = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
            detail = Trim(output);
            return true;
        }

        private static bool CreateFromXml(string exe, out string detail)
        {
            string xmlPath = Path.Combine(Path.GetTempPath(),
                "ClassFirewall-" + Guid.NewGuid().ToString("N") + ".xml");

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

        /// <summary>
        /// 任务定义。刻意不写 UserId（省略时用注册该任务的用户）；
        /// ExecutionTimeLimit=PT0S 避免默认 72 小时被强杀，
        /// 电池两项设为 false 避免笔记本上不启动。
        /// </summary>
        internal static string BuildTaskXml(string exe)
        {
            return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Class Firewall 开机自启</Description>
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
                    return (-1, "schtasks 超时");

                return (p.ExitCode, (stdout + " " + stderr).Trim());
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }
    }
}
