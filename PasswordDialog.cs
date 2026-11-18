using System;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ClassFirewall
{
    /// <summary>
    /// 密码输入对话框（独立小窗口）。两种用法：
    ///   · VerifyAsync —— 单个输入框，用来解锁 / 退出程序
    ///   · SetNewAsync  —— 两个输入框（新密码 + 确认），留空表示取消密码保护
    /// </summary>
    internal sealed class PasswordDialog : DialogWindow
    {
        private readonly PasswordBox _first;
        private readonly PasswordBox? _second;
        private readonly TextBlock _error;
        private readonly bool _allowEmpty;
        private readonly bool _confirmMode;

        private string? _result;
        private bool _accepted;

        private PasswordDialog(string title, string prompt, bool confirmMode, bool allowEmpty)
            : base(title, 420, confirmMode ? 300 : 250)
        {
            _allowEmpty = allowEmpty;
            _confirmMode = confirmMode;

            BuildHeader(title, prompt);

            var body = new StackPanel { Spacing = 10, Margin = new Thickness(24, 16, 24, 0) };
            Grid.SetRow(body, 2);

            _first = new PasswordBox
            {
                PasswordRevealMode = PasswordRevealMode.Peek,
                PlaceholderText = confirmMode ? "新密码" : "请输入密码",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _first.KeyDown += OnKeyDown;
            body.Children.Add(_first);

            if (confirmMode)
            {
                _second = new PasswordBox
                {
                    PasswordRevealMode = PasswordRevealMode.Peek,
                    PlaceholderText = "再输入一次确认",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                _second.KeyDown += OnKeyDown;
                body.Children.Add(_second);
            }

            _error = new TextBlock
            {
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            body.Children.Add(_error);

            Layout.Children.Add(body);

            BuildButtons("确定", "取消", OnAccept, Cancel);

            // Enter = 确定，Esc = 取消（问号框两个键都能用）
            var enter = new KeyboardAccelerator { Key = VirtualKey.Enter };
            enter.Invoked += (_, e) => { e.Handled = true; OnAccept(); };
            Layout.KeyboardAccelerators.Add(enter);

            var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
            escape.Invoked += (_, e) => { e.Handled = true; Cancel(); };
            Layout.KeyboardAccelerators.Add(escape);

            FirstInput = _first;

            AutomationProperties.SetName(_first, confirmMode ? "新密码" : "密码");
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                OnAccept();
            }
        }

        private void OnAccept()
        {
            string first = _first.Password;
            string? second = _second?.Password;

            if (_confirmMode)
            {
                if (first.Length == 0 && !_allowEmpty)
                {
                    ShowError("密码不能为空");
                    return;
                }
                if (!string.Equals(first, second, StringComparison.Ordinal))
                {
                    ShowError("两次输入不一致");
                    return;
                }
            }
            else if (first.Length == 0)
            {
                ShowError("请输入密码");
                return;
            }

            _result = first;
            _accepted = true;
            CloseDialog();
        }

        private void ShowError(string text)
        {
            _error.Text = text;
            _error.Visibility = Visibility.Visible;
            _first.Focus(FocusState.Programmatic);
            _first.SelectAll();
        }

        private void Cancel()
        {
            _accepted = false;
            _result = null;
            CloseDialog();
        }

        /// <summary>弹出「输入密码」框。取消返回 null</summary>
        public static async Task<string?> VerifyAsync(Window? owner, string title = "需要密码",
            string prompt = "请输入解锁密码：")
        {
            var dlg = new PasswordDialog(title, prompt, confirmMode: false, allowEmpty: false);
            dlg.AttachOwner(owner);
            await dlg.ShowAsync();
            return dlg._accepted ? dlg._result : null;
        }

        /// <summary>
        /// 弹出「设置新密码」框。
        /// 返回 true 表示用户确认了；<paramref name="newPassword"/> 为空串表示取消密码保护。
        /// </summary>
        public static async Task<(bool ok, string newPassword)> SetNewAsync(Window? owner,
            string prompt = "设置一个密码，之后打开本程序需要输入它才能解锁。\n（留空并确定 = 取消密码保护）")
        {
            var dlg = new PasswordDialog("设置密码", prompt, confirmMode: true, allowEmpty: true);
            dlg.AttachOwner(owner);
            await dlg.ShowAsync();
            return (dlg._accepted, dlg._accepted ? dlg._result ?? "" : "");
        }
    }
}
