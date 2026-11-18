using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace ClassFirewall
{
    public partial class App : Application
    {
        /// <summary>供对话框定位父窗口（未创建主窗口时为空）</summary>
        internal static Window? MainWindow { get; private set; }

        internal static App? Instance { get; private set; }

        /// <summary>主界面当前主题，对话框打开时跟随它</summary>
        internal static AppTheme CurrentTheme { get; set; } = AppTheme.System;

        public App()
        {
            Instance = this;
            InitializeComponent();

            UnhandledException += (_, e) =>
            {
                Program.WriteCrash("WinUI UnhandledException", e.Exception);
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            var settings = SettingsStore.Load();

            // 密码校验放在窗口出现之前 —— 没解锁连界面都不给看
            if (!Program.SilentMode && !await UnlockAsync(settings))
            {
                Exit();
                return;
            }

            var window = new MainWindow(settings);
            MainWindow = window;

            if (Program.SilentMode)
                window.StartHiddenInTray();
            else
                window.Activate();
        }

        /// <summary>启动前的解锁流程：最多 5 次，全错则退出</summary>
        private static async Task<bool> UnlockAsync(AppSettings settings)
        {
            if (!PasswordGate.IsConfigured(settings.PasswordHash))
                return true;

            const int maxAttempts = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string prompt = attempt == 1
                    ? "本程序已设置密码，请输入以解锁："
                    : $"密码不对，再试一次（第 {attempt}/{maxAttempts} 次）：";

                string? input = await PasswordDialog.VerifyAsync(
                    owner: null,
                    title: "Class Firewall 已锁定",
                    prompt: prompt);

                if (input == null) return false;                       // 点了取消
                if (PasswordGate.Verify(input, settings.PasswordHash)) return true;
            }

            await MessageWindow.ShowAsync(
                owner: null,
                title: "密码错误",
                message: $"密码连续输错 {maxAttempts} 次，程序退出。\n\n" +
                         "如果忘记了密码，可以用万能密码解锁（见 README 的「密码保护」一节）。",
                primaryText: "确定",
                kind: MessageKind.Warning);

            return false;
        }
    }
}
