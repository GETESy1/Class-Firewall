using System;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ClassFirewall
{
    internal enum MessageKind
    {
        Info,
        Warning,
        Error,
        Question
    }

    internal enum MessageResult
    {
        Primary,
        Secondary
    }

    /// <summary>
    /// 通用消息窗口，替代 WinForms 的 MessageBox。
    ///
    /// 支持：
    ///   · 单按钮（提示 / 错误）
    ///   · 双按钮（确认 / 取消、是 / 否）
    ///   · 可选的「不再询问」复选框
    /// </summary>
    internal sealed class MessageWindow : DialogWindow
    {
        private MessageResult _result = MessageResult.Secondary;
        private CheckBox? _dontAskAgain;

        private MessageWindow(string title, string message, MessageKind kind,
            string primaryText, string? secondaryText, string? dontAskAgainText, bool wide)
            : base(title, wide ? 540 : 460, EstimateHeight(message, dontAskAgainText != null))
        {
            // ---- 图标 + 文本 ----
            var body = new Grid { Margin = new Thickness(24, 22, 24, 0), ColumnSpacing = 14 };
            Grid.SetRow(body, 0);

            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var (glyph, colorKey) = kind switch
            {
                MessageKind.Warning => ("\uE7BA", "SystemFillColorCautionBrush"),
                MessageKind.Error => ("\uEA39", "SystemFillColorCriticalBrush"),
                MessageKind.Question => ("\uE897", "AccentTextFillColorPrimaryBrush"),
                _ => ("\uE946", "AccentTextFillColorPrimaryBrush")
            };

            var icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 26,
                Foreground = (Brush)Application.Current.Resources[colorKey],
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetColumn(icon, 0);
            body.Children.Add(icon);

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                IsTextSelectionEnabled = true
            });

            if (dontAskAgainText != null)
            {
                _dontAskAgain = new CheckBox
                {
                    Content = dontAskAgainText,
                    Margin = new Thickness(-8, 12, 0, 0)
                };
                stack.Children.Add(_dontAskAgain);
            }

            Grid.SetColumn(stack, 1);
            body.Children.Add(stack);

            Layout.Children.Add(body);

            // ---- 按钮 ----
            var (primary, secondary) = BuildButtons(primaryText, secondaryText ?? "取消",
                () => { _result = MessageResult.Primary; CloseDialog(); },
                () => { _result = MessageResult.Secondary; CloseDialog(); });

            if (secondaryText == null)
                secondary.Visibility = Visibility.Collapsed;

            var enter = new KeyboardAccelerator { Key = VirtualKey.Enter };
            enter.Invoked += (_, e) =>
            {
                e.Handled = true;
                _result = MessageResult.Primary;
                CloseDialog();
            };
            Layout.KeyboardAccelerators.Add(enter);

            var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
            escape.Invoked += (_, e) =>
            {
                e.Handled = true;
                _result = MessageResult.Secondary;
                CloseDialog();
            };
            Layout.KeyboardAccelerators.Add(escape);
        }

        private static int EstimateHeight(string message, bool hasCheckbox)
        {
            // 粗略估算：每行约 20px，按 26 个字符折行；标题另占一行
            int breaks = 0;
            foreach (char c in message) if (c == '\n') breaks++;
            int lines = Math.Clamp((message.Length / 26) + breaks + 1, 1, 16);
            int height = 118 + lines * 20 + (hasCheckbox ? 34 : 0);
            return Math.Clamp(height, 200, 560);
        }

        private static async Task<MessageResult> ShowCoreAsync(
            Window? owner, string title, string message, MessageKind kind,
            string primaryText, string? secondaryText, string? dontAskAgainText,
            bool wide, Action<bool>? onDontAskAgain)
        {
            var dlg = new MessageWindow(title, message, kind, primaryText, secondaryText, dontAskAgainText, wide);
            dlg.AttachOwner(owner);
            await dlg.ShowAsync();

            onDontAskAgain?.Invoke(dlg._dontAskAgain?.IsChecked == true);
            return dlg._result;
        }

        // ---------------- 便捷入口 ----------------

        /// <summary>只有确定按钮的提示框</summary>
        public static Task ShowAsync(Window? owner, string title, string message,
            string primaryText = "确定", MessageKind kind = MessageKind.Info)
            => ShowCoreAsync(owner, title, message, kind, primaryText, null, null, false, null);

        /// <summary>两个按钮：返回 true 表示点了主按钮（是 / 确定）</summary>
        public static async Task<bool> ConfirmAsync(Window? owner, string title, string message,
            string primaryText = "确定", string secondaryText = "取消",
            MessageKind kind = MessageKind.Question,
            string? dontAskAgainText = null,
            Action<bool>? onDontAskAgain = null,
            bool wide = false)
        {
            var result = await ShowCoreAsync(owner, title, message, kind,
                primaryText, secondaryText, dontAskAgainText, wide, onDontAskAgain);

            return result == MessageResult.Primary;
        }
    }
}
