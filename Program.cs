using System;
using System.Linq;
using System.Windows.Forms;

namespace ClassFirewall
{
    internal static class Program
    {
        public static bool SilentMode { get; private set; }

        /// <summary>解锁失败次数上限，超过就退出，避免无限尝试</summary>
        private const int MaxAttempts = 5;

        [STAThread]
        static void Main(string[] args)
        {
            SilentMode = args.Any(a =>
                a.Equals("-silent", StringComparison.OrdinalIgnoreCase));

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
