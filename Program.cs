using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ClassFirewall
{
    internal static class Program
    {
        public static bool SilentMode { get; private set; }

        /// <summary>解锁失败次数上限，超过就退出，避免无限尝试</summary>
        private const int MaxAttempts = 5;

        /// <summary>崩溃日志：开机自启出问题时，这是唯一的线索来源</summary>
        private static readonly string CrashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassFirewall", "crash.log");

        [STAThread]
        static void Main(string[] args)
        {
            SilentMode = args.Any(a =>
                a.Equals("-silent", StringComparison.OrdinalIgnoreCase));

            InstallCrashLogging();

            ApplicationConfiguration.Initialize();

            // ★ 解锁校验放在窗口出现之前，避免没解锁就瞥见界面。
            //   开机自启（-silent）不弹窗，否则会在登录时卡住等人输密码。
            if (!SilentMode && !Unlock())
                return;

            var form = new MainForm();
            if (SilentMode)
                form.WindowState = FormWindowState.Minimized;

            Application.Run(form);
        }

        /// <summary>
        /// 把未处理异常写进 %AppData%\ClassFirewall\crash.log。
        ///
        /// 注意：**栈溢出（0xC00000FD）无法被捕获** —— 进程会被直接终止，
        /// 本方法对它无能为力，只能靠消除递归来修。
        /// 但其它异常都能留下记录，不至于像之前那样"程序就是没反应、毫无提示"。
        /// </summary>
        private static void InstallCrashLogging()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

            Application.ThreadException += (_, e) =>
                WriteCrash("WinForms ThreadException", e.Exception);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        }

        private static void WriteCrash(string source, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);

                var sb = new StringBuilder();
                sb.AppendLine("==================== 崩溃 ====================");
                sb.AppendLine($"时间    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"来源    : {source}");
                sb.AppendLine($"静默模式: {SilentMode}");
                sb.AppendLine($"命令行  : {Environment.CommandLine}");
                sb.AppendLine($"异常    : {ex?.GetType().FullName ?? "(非 Exception 对象)"}");
                sb.AppendLine($"消息    : {ex?.Message}");
                sb.AppendLine(ex?.StackTrace);

                File.AppendAllText(CrashLog, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* 记日志本身绝不能抛 */ }
        }

        /// <summary>没有设密码直接放行；设了就要求输入，最多 MaxAttempts 次</summary>
        private static bool Unlock()
        {
            var settings = SettingsStore.Load();
            if (!PasswordGate.IsConfigured(settings.PasswordHash))
                return true;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                string prompt = attempt == 1
                    ? "本程序已设置密码，请输入以解锁："
                    : $"密码不对，再试一次（第 {attempt}/{MaxAttempts} 次）：";

                string? input = PasswordDialog.Verify(null, "Class Firewall 已锁定", prompt);
                if (input == null) return false;                        // 点了取消

                if (PasswordGate.Verify(input, settings.PasswordHash))
                    return true;
            }

            MessageBox.Show(
                $"密码连续输错 {MaxAttempts} 次，程序退出。\n\n" +
                "如果忘记了密码，可以用万能密码解锁（见 README 的「密码保护」一节）。",
                "Class Firewall",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return false;
        }
    }
}
