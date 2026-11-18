using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace ClassFirewall
{
    /// <summary>
    /// 所有自绘对话框的公共外壳。
    ///
    /// 为什么不用 ContentDialog：程序启动时的「解锁」发生在主窗口创建之前，
    /// 这时没有 XamlRoot 可以挂 ContentDialog。所以对话框做成独立的小窗口，
    /// 无论主窗口在不在都能弹出来。
    /// </summary>
    internal abstract class DialogWindow : Window
    {
        protected readonly Grid RootGrid = new();
        protected readonly Grid Layout = new();
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Window? _owner;

        protected DialogWindow(string title, int width, int height)
        {
            Title = title;

            RootGrid.Children.Add(Layout);
            Content = RootGrid;

            Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 标题
            Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 说明
            Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容
            Layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 按钮

            try
            {
                AppWindow.SetIcon(Environment.ProcessPath ?? "");
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }
            catch { }

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(true, true);
            }

            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            AppWindow.MoveAndResize(new RectInt32(
                area.WorkArea.X + (area.WorkArea.Width - width) / 2,
                area.WorkArea.Y + (area.WorkArea.Height - height) / 2,
                width, height));

            Closed += (_, __) => _closed.TrySetResult();

            // 对话框跟随主界面当前主题
            ApplyTheme(App.CurrentTheme);
        }

        /// <summary>
        /// 把主题应用到整个对话框（根 Grid）。
        ///
        /// 必须连背景一起重设：Application.Resources 里取到的画刷是「取值那一刻」的
        /// 快照，不会跟着 RequestedTheme 变，否则就是「浅色底 + 白色字」。
        /// </summary>
        public void ApplyTheme(AppTheme theme)
        {
            ThemeHelper.Apply(RootGrid, theme);

            bool light = ThemeHelper.Resolve(theme) == ElementTheme.Light;
            RootGrid.Background = new SolidColorBrush(light
                ? Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3)   // 浅色：Windows 11 页面底色
                : Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20));  // 深色
        }

        protected void BuildHeader(string? caption, string? description)
        {
            var panel = new StackPanel { Spacing = 6, Margin = new Thickness(24, 20, 24, 0) };
            Grid.SetRow(panel, 0);

            if (!string.IsNullOrEmpty(caption))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = caption,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrEmpty(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    // 用透明度而不是具体画刷：画刷是取值那一刻的快照，
                    // 主题切换后不会跟着变（会变成浅底白字那种事故）
                    Opacity = 0.7
                });
            }

            Layout.Children.Add(panel);
        }

        private UIElement? _firstInput;

        /// <summary>设置对话框打开后自动获得焦点的控件</summary>
        protected UIElement FirstInput
        {
            set
            {
                _firstInput = value;
                Activated += (_, __) => value.Focus(FocusState.Programmatic);
            }
        }

        /// <summary>构造「按钮行」，返回主/次两个按钮，便于调用方调整</summary>
        protected (Button primary, Button secondary) BuildButtons(string primaryText, string secondaryText,
            Action onPrimary, Action onSecondary)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(24, 8, 24, 20)
            };
            Grid.SetRow(panel, 3);

            var primary = new Button
            {
                Content = primaryText,
                MinWidth = 88,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            primary.Click += (_, __) => onPrimary();

            var secondary = new Button { Content = secondaryText, MinWidth = 88 };
            secondary.Click += (_, __) => onSecondary();

            panel.Children.Add(primary);
            panel.Children.Add(secondary);
            Layout.Children.Add(panel);

            return (primary, secondary);
        }

        /// <summary>把对话框挂在指定窗口上（成为其子窗口，跟随位置/层级）</summary>
        public void AttachOwner(Window? owner)
        {
            _owner = owner;
            if (owner == null) return;

            try
            {
                IntPtr ownerHwnd = WindowNative.GetWindowHandle(owner);
                IntPtr hwnd = WindowNative.GetWindowHandle(this);
                SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, ownerHwnd);
            }
            catch { }
        }

        /// <summary>展示对话框；窗口关闭时任务结束</summary>
        public async Task ShowAsync()
        {
            Activate();
            if (_owner != null) AttachOwner(_owner);
            await _closed.Task;
        }

        protected void CloseDialog()
        {
            try { Close(); } catch { }
        }

        private const int GWLP_HWNDPARENT = -8;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, value)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }
}
