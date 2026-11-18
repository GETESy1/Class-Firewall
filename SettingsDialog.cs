using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClassFirewall
{
    /// <summary>「设置」对话框：主题、密码、打开配置目录</summary>
    internal sealed class SettingsDialog : DialogWindow
    {
        private readonly RadioButtons _themes = new();
        private readonly TextBlock _passwordState = new();

        public AppTheme SelectedTheme { get; private set; }

        /// <summary>点了「设置密码」，由调用方负责后续流程</summary>
        public bool RequestPasswordChange { get; private set; }

        public bool RequestOpenConfigFolder { get; private set; }

        public SettingsDialog(AppTheme currentTheme, bool passwordConfigured, Window? owner)
            : base("设置", 480, 560)
        {
            SelectedTheme = currentTheme;

            // 先把主题落到根元素上，再建内容 —— 否则内容里的主题画刷会按默认主题解析
            ApplyTheme(currentTheme);

            BuildHeader("设置", "这里的改动会立即生效，并保存到 settings.json。");

            var body = new StackPanel { Spacing = 18, Margin = new Thickness(24, 16, 24, 0) };
            Grid.SetRow(body, 2);

            // ---------- 主题 ----------
            body.Children.Add(SectionTitle("界面主题"));

            _themes.MaxColumns = 3;
            foreach (var opt in ThemeHelper.Options)
                _themes.Items.Add(new RadioButton { Content = opt.Label, Tag = opt.Value });

            for (int i = 0; i < _themes.Items.Count; i++)
            {
                if (_themes.Items[i] is RadioButton rb && rb.Tag is AppTheme t && t == currentTheme)
                    _themes.SelectedIndex = i;
            }

            _themes.SelectionChanged += (_, __) =>
            {
                if (_themes.SelectedItem is RadioButton rb && rb.Tag is AppTheme t)
                {
                    SelectedTheme = t;
                    ApplyTheme(t);   // 对话框内即时预览
                }
            };
            body.Children.Add(_themes);

            body.Children.Add(Divider());

            // ---------- 密码 ----------
            body.Children.Add(SectionTitle("密码保护"));

            _passwordState.Text = passwordConfigured
                ? "已启用：打开界面、退出程序都需要密码。忘记密码可用万能密码解锁。"
                : "未设置：任何使用者都能打开界面、关掉屏蔽。建议在教室机器上设置。";
            _passwordState.FontSize = 12;
            _passwordState.TextWrapping = TextWrapping.Wrap;
            if (passwordConfigured)
            {
                // 不设 Foreground（设成 null 会把主题前景色顶掉，深色下就看不见了）
                _passwordState.Opacity = 0.75;
            }
            else
            {
                // 固定琥珀色：浅色/深色底上都看得清
                _passwordState.Foreground = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0xE0, 0x9A, 0x2B));
            }
            body.Children.Add(_passwordState);

            var pwdButton = new Button
            {
                Content = passwordConfigured ? "修改 / 取消密码" : "设置密码",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            pwdButton.Click += (_, __) =>
            {
                RequestPasswordChange = true;
                CloseDialog();
            };
            body.Children.Add(pwdButton);

            body.Children.Add(Divider());

            // ---------- 配置文件 ----------
            body.Children.Add(SectionTitle("配置文件"));

            body.Children.Add(new TextBlock
            {
                Text = SettingsStore.SettingsPath,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Opacity = 0.7
            });

            var openFolder = new Button
            {
                Content = "打开配置目录",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            openFolder.Click += (_, __) =>
            {
                RequestOpenConfigFolder = true;
                CloseDialog();
            };
            body.Children.Add(openFolder);

            Layout.Children.Add(body);

            // ---------- 按钮 ----------
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 8, 24, 20)
            };
            Grid.SetRow(panel, 3);

            var done = new Button
            {
                Content = "完成",
                MinWidth = 96,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            done.Click += (_, __) => CloseDialog();
            panel.Children.Add(done);
            Layout.Children.Add(panel);

            Closed += (_, __) => { };
        }

        private static TextBlock SectionTitle(string text) => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        };

        private static Border Divider() => new()
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
        };

        public static async Task<(AppTheme theme, bool passwordChange, bool openConfig)> ShowAsync(
            Window? owner, AppTheme currentTheme, bool passwordConfigured)
        {
            var dlg = new SettingsDialog(currentTheme, passwordConfigured, owner);
            dlg.AttachOwner(owner);
            await dlg.ShowAsync();
            return (dlg.SelectedTheme, dlg.RequestPasswordChange, dlg.RequestOpenConfigFolder);
        }
    }
}
