using System;
using System.Diagnostics;
using System.IO;

namespace ClassFirewall
{
    public static class AutoStartManager
    {
        private const string TaskName = "ClassFirewallAutoStart";

        public static bool IsEnabled()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool Enable()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return false;

                Disable(); // 先删旧的，避免重复

                string args =
                    $"/Create /TN \"{TaskName}\" " +
                    $"/TR \"\\\"{exe}\\\" -silent\" " +
                    "/SC ONLOGON /RL HIGHEST /F";

                var psi = new ProcessStartInfo("schtasks", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(8000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool Disable()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks",
                    $"/Delete /TN \"{TaskName}\" /F")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
                return true;
            }
            catch { return false; }
        }
    }
}