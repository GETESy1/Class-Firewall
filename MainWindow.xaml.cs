using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace ClassFirewall
{
    /// <summary>
    /// 主窗口（WinUI 3）。
    ///
    /// 屏蔽原理不变（只有一层，尽量简单可靠）：
    ///   1. 本机监听 127.0.0.1:53 当 DNS 服务器
    ///   2. 把系统网卡 DNS 指向 127.0.0.1
    ///   3. 命中黑名单的域名一律解析到 127.0.0.1 → 网站打不开
    ///   4. 未命中的查询转发上游，正常上网不受影响
    ///
    /// 与 WinForms 版的等价行为：
    ///   · 最小化 → 收进托盘；设了密码时点 × 也收进托盘
    ///   · 托盘双击 / 右键「打开主界面」→ 过密码后显示
    ///   · 托盘「退出程序」→ 过密码后真正退出，并还原系统 DNS
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const string AppTitle = "Class Firewall";

        private readonly DnsServer _dnsServer = new DnsServer(53);
        private readonly AppSettings _settings;
        private readonly MainViewModel _vm = new();
        private readonly ObservableCollection<string> _logLines = new();

        private bool _loadingSettings;
        private bool _suppressToggle;
        private bool _startupStarted;

        private bool _silentStart;
        private bool _reallyExit;
        private bool _cleanupDone;
        private bool _closing;

        private string _passwordHash = "";

        private TrayIcon? _tray;
        private WindowCloseHook? _closeHook;

        public MainWindow(AppSettings settings)
        {
            _settings = settings;

            InitializeComponent();

            TryInitWindow();
            BuildViewModel();

            LogList.ItemsSource = _logLines;

            ClearBtn.Click += async (_, __) => await ClearAllAsync();
            SelectNoneButton.Click += (_, __) => SelectNone();
            ClearLogButton.Click += (_, __) => _logLines.Clear();
            FlushBtn.Click += (_, __) =>
            {
                DnsConfigurator.FlushCache();
                SafeLog("已刷新 DNS 缓存");
                SetStatus("DNS 缓存已刷新。");
            };
            SettingsBtn.Click += async (_, __) => await OpenSettingsAsync();

            BlockToggle.Toggled += (_, __) => OnBlockToggled();
            AutoStartToggle.Toggled += (_, __) => OnAutoStartToggled();
            AutoBlockToggle.Toggled += (_, __) => { if (!_loadingSettings && !_suppressToggle) SaveSettings(); };
            DohToggle.Toggled += async (_, __) => await OnDohToggledAsync();

            _dnsServer.OnBlocked += d => SafeLog($"已拦截: {d}");
            _dnsServer.OnError += msg => SafeLog($"DNS 错误: {msg}");

            RestoreFromSettings();

            SetupTray();
            SetupCloseHook();

            // 窗口第一次激活后跑启动流程（清理遗留、同步自启、恢复屏蔽等）
            Activated += (_, __) =>
            {
                if (_startupStarted) return;
                _startupStarted = true;
                _ = OnReadyAsync();
            };
        }

        // ==================== 初始化 ====================

        private void TryInitWindow()
        {
            try
            {
                AppWindow.Title = "Class Firewall — 班级网络防火墙";
                AppWindow.SetIcon(Environment.ProcessPath ?? "");

                try
                {
                    TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri("ms-appx:///ico/app.ico"));
                }
                catch { }

                // 自绘标题栏
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(AppTitleBar);

                // 给系统的最小化/最大化/关闭按钮让出位置，
                // 否则右上角的状态徽标会被它们盖住（深色主题下尤其明显）
                ApplyCaptionInset();

                // Mica（Win11）/ 亚克力（Win10）背景；不支持的机器自动忽略
                try { SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); }
                catch { }

                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsResizable = true;
                    presenter.IsMaximizable = true;
                }

                ResizeToDesignSize();
            }
            catch (Exception ex)
            {
                Program.WriteCrash("MainWindow.TryInitWindow", ex);
            }
        }

        /// <summary>
        /// 按系统标题栏按钮的实际宽度，给 AppTitleBar 右侧留出内边距。
        /// RightInset 是物理像素，而 XAML 用的是 DIP，所以要除以 DPI 缩放。
        /// </summary>
        private void ApplyCaptionInset()
        {
            try
            {
                double scale = 1.0;
                try
                {
                    var hwnd = WindowNative.GetWindowHandle(this);
                    scale = GetDpiForWindow(hwnd) / 96.0;
                    if (scale <= 0) scale = 1.0;
                }
                catch { }

                double rightInset = AppWindow.TitleBar.RightInset / scale;
                if (rightInset < 100) rightInset = 140;   // 拿不到就当三个按钮的宽度

                AppTitleBar.Padding = new Thickness(16, 0, 12 + rightInset, 0);
            }
            catch { }
        }

        /// <summary>按 DPI 比例把窗口撑到设计尺寸（WinUI 的尺寸单位是物理像素）</summary>
        private void ResizeToDesignSize()
        {
            try
            {
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                double scale = 1.0;
                try
                {
                    var hwnd = WindowNative.GetWindowHandle(this);
                    scale = GetDpiForWindow(hwnd) / 96.0;
                    if (scale <= 0) scale = 1.0;
                }
                catch { }

                int w = (int)Math.Min(1000 * scale, area.WorkArea.Width * 0.92);
                int h = (int)Math.Min(760 * scale, area.WorkArea.Height * 0.92);

                AppWindow.MoveAndResize(new RectInt32(
                    area.WorkArea.X + (area.WorkArea.Width - w) / 2,
                    area.WorkArea.Y + (area.WorkArea.Height - h) / 2,
                    w, h));
            }
            catch { }
        }

        private void BuildViewModel()
        {
            SiteList.ItemsSource = _vm.Sites;

            foreach (var item in _vm.Sites)
            {
                if (_settings.CheckedSites.Contains(item.Name))
                    item.IsChecked = true;
            }

            SiteCountText.Text = $"共 {_vm.Sites.Count} 个站点";
        }

        private void RestoreFromSettings()
        {
            _loadingSettings = true;
            try
            {
                _suppressToggle = true;
                BlockToggle.IsOn = _settings.DnsEnabled;
                AutoStartToggle.IsOn = _settings.AutoStart;
                AutoBlockToggle.IsOn = _settings.AutoBlock;
                DohToggle.IsOn = _settings.BlockDoh;
                _suppressToggle = false;

                _passwordHash = _settings.PasswordHash;

                ApplyTheme(_settings.Theme);
            }
            finally
            {
                _loadingSettings = false;
            }

            // 恢复完再挂勾选回调，避免初始化时触发同步
            foreach (var item in _vm.Sites)
                item.CheckedChanged += OnSiteCheckedChanged;

            RefreshPreview();
            UpdateChrome();
        }

        private void ApplyTheme(AppTheme theme)
        {
            App.CurrentTheme = theme;
            ThemeHelper.Apply(RootGrid, theme);
        }

        private async Task OnReadyAsync()
        {
            // 截图/UI 自检模式：跳过所有需要管理员权限或会改系统状态的启动动作
            if (Environment.GetEnvironmentVariable("CLASSFIREWALL_UISNAP") == "1")
            {
                SafeLog("UI 自检模式：已跳过系统改动");
                RefreshPreview();
                return;
            }

            // 升级清理：旧版本遗留的 hosts 屏蔽块与防火墙规则
            PurgeLegacyHosts();
            await Task.Run(PurgeLegacyFirewallRules);

            // 开机自启开关以任务计划程序中的真实状态为准
            ReconcileAutoStartToggle();

            if (_silentStart)
            {
                MinimizeToTray();
            }
            else if (!PasswordGate.IsConfigured(_passwordHash))
            {
                await MaybeOfferPasswordSetupAsync();
            }

            LogLockState();

            // 静默启动也必须真正恢复屏蔽 —— 开机自启的意义就在这里
            if (_settings.AutoBlock && _settings.DnsEnabled)
                EnableBlocking(silent: _silentStart);
        }

        /// <summary>静默启动（开机自启）用：窗口不出现，直接进托盘</summary>
        public void StartHiddenInTray()
        {
            _silentStart = true;
            try { AppWindow.Hide(); } catch { }
            MinimizeToTray();
        }

        // ==================== 托盘 ====================

        private void SetupTray()
        {
            _tray = new TrayIcon(
                "Class Firewall — 网站屏蔽",
                onOpen: () => _ = RestoreFromTrayAsync(),
                onExit: () => _ = TryExitFromTrayAsync());
        }

        private void MinimizeToTray()
        {
            try
            {
                _tray?.Show();
                AppWindow.Hide();
            }
            catch (Exception ex)
            {
                Program.WriteCrash("MainWindow.MinimizeToTray", ex);
            }
        }

        private async Task RestoreFromTrayAsync()
        {
            // 打开界面要过密码，否则设了密码也白设
            if (!await RequirePasswordAsync("打开主界面")) return;

            _tray?.Hide();

            try
            {
                AppWindow.Show();
                Activate();

                // 兜底：位置跑到屏幕外时拉回主屏中央
                var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                var pos = AppWindow.Position;
                var size = AppWindow.Size;
                var work = area.WorkArea;
                bool onScreen = pos.X + size.Width > work.X && pos.X < work.X + work.Width &&
                                pos.Y + size.Height > work.Y && pos.Y < work.Y + work.Height;
                if (!onScreen) ResizeToDesignSize();
            }
            catch (Exception ex)
            {
                Program.WriteCrash("MainWindow.RestoreFromTray", ex);
            }
        }

        private async Task TryExitFromTrayAsync()
        {
            if (!await RequirePasswordAsync("退出程序")) return;

            _reallyExit = true;
            Close();
        }

        private void SetupCloseHook()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                _closeHook = new WindowCloseHook(hwnd, OnWindowCloseRequested);
            }
            catch (Exception ex)
            {
                Program.WriteCrash("MainWindow.SetupCloseHook", ex);
            }
        }

        /// <summary>
        /// WM_CLOSE 的拦截：设了密码时点 × 不退出，而是收进托盘继续屏蔽 ——
        /// 否则使用者一点叉号就把屏蔽关掉了，密码就白设了。
        /// </summary>
        private void OnWindowCloseRequested()
        {
            if (_reallyExit || _cleanupDone || !PasswordGate.IsConfigured(_passwordHash))
            {
                _closeHook?.Dispose();
                CleanupOnExit();
                return;
            }

            if (_closing) return;
            _closing = true;

            try
            {
                MinimizeToTray();
                SetStatus("程序仍在后台屏蔽。要退出请右键托盘图标。");
                SafeLog("点关闭不会退出：已收进托盘继续屏蔽（设了密码）");
            }
            finally
            {
                _closing = false;
            }
        }

        private void CleanupOnExit()
        {
            if (_cleanupDone) return;
            _cleanupDone = true;

            SaveSettings();

            try { _tray?.Dispose(); } catch { }
            _tray = null;

            // 只要接管过系统 DNS 就必须还原，否则关掉程序就断网
            if (BlockToggle.IsOn || _dnsServer.IsRunning)
            {
                try { _dnsServer.Stop(); } catch { }
                try { DnsConfigurator.RestoreDhcpDns(); } catch { }
                try { DnsConfigurator.FlushCache(); } catch { }
            }
        }

        // ==================== 密码 ====================

        private async Task<bool> RequirePasswordAsync(string action)
        {
            if (!PasswordGate.IsConfigured(_passwordHash)) return true;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                string prompt = attempt == 1
                    ? $"「{action}」需要密码，请输入："
                    : $"密码不对，再试一次（第 {attempt}/3 次）：";

                string? input = await PasswordDialog.VerifyAsync(this, "需要密码", prompt);
                if (input == null) return false;
                if (PasswordGate.Verify(input, _passwordHash)) return true;
            }

            SafeLog("警告：密码连续输错 3 次，操作已取消");
            return false;
        }

        private void LogLockState()
        {
            SafeLog(PasswordGate.IsConfigured(_passwordHash)
                ? "已启用密码保护"
                : "未设置密码");

            UpdateChrome();
        }

        /// <summary>
        /// 首次运行（还没设密码）时提示设置一次。
        /// 只是「提示」，用户可以直接跳过 —— 不该拦着人用工具。
        /// </summary>
        private async Task MaybeOfferPasswordSetupAsync()
        {
            bool yes = await MessageWindow.ConfirmAsync(this,
                "设置密码",
                "要不要给本程序设一个密码？\n\n" +
                "· 设了之后，打开界面、退出程序都需要输入密码\n" +
                "· 这样使用者就没法自己把屏蔽关掉\n" +
                "· 忘记密码可以用万能密码解锁（见 README）\n" +
                "· 之后也可以随时在「设置」里修改",
                primaryText: "设置密码", secondaryText: "以后再说");

            if (yes) await ChangePasswordAsync();
        }

        /// <summary>设置 / 修改 / 取消密码。已有密码时先要求验证原密码</summary>
        private async Task ChangePasswordAsync()
        {
            try
            {
                // 已经设过密码：必须先证明自己是管理员
                if (PasswordGate.IsConfigured(_passwordHash) && !await RequirePasswordAsync("修改密码"))
                    return;

                string prompt = PasswordGate.IsConfigured(_passwordHash)
                    ? "输入新密码。\n（留空并确定 = 取消密码保护）"
                    : "设置一个密码，之后打开本程序需要输入它才能解锁。\n（留空并确定 = 不设密码）";

                var (ok, newPassword) = await PasswordDialog.SetNewAsync(this, prompt);
                if (!ok) return;

                if (newPassword.Length == 0)
                {
                    _passwordHash = "";
                    SafeLog("已取消密码保护");
                    SetStatus("密码保护已取消。");
                }
                else
                {
                    _passwordHash = PasswordGate.CreateHash(newPassword);
                    SafeLog("密码已设置：打开界面与退出都需要密码");
                    SetStatus("密码已设置。");
                }

                SaveSettings();
                UpdateChrome();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("设置密码失败：" + ex.Message);
            }
        }

        // ==================== 设置对话框 ====================

        private async Task OpenSettingsAsync()
        {
            var (theme, wantPassword, openConfig) =
                await SettingsDialog.ShowAsync(this, _settings.Theme, PasswordGate.IsConfigured(_passwordHash));

            if (theme != _settings.Theme)
            {
                _settings.Theme = theme;
                ApplyTheme(theme);
                SaveSettings();
                SafeLog($"界面主题已切换为「{ThemeHelper.DisplayName(theme)}」");
            }

            if (openConfig)
            {
                try
                {
                    var dir = Path.GetDirectoryName(SettingsStore.SettingsPath)!;
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    await ShowErrorAsync("打开配置目录失败：" + ex.Message);
                }
            }

            if (wantPassword) await ChangePasswordAsync();
        }

        // ==================== 站点勾选 ====================

        /// <summary>
        /// 整行点击 = 勾选/取消勾选。
        ///
        /// 模板里的 CheckBox 设了 IsHitTestVisible=False，点击事件落到 ListViewItem 上，
        /// 由这里翻转状态；这样点文字、点空白处都能勾上（比只能点小方框好用）。
        /// 键盘：ListView 在 IsItemClickEnabled=true 时，Enter/Space 也会触发这里。
        /// </summary>
        private void OnSiteItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SiteItem item)
                item.IsChecked = !item.IsChecked;
        }

        private void SelectNone()
        {
            foreach (var site in _vm.Sites.Where(s => s.IsChecked).ToList())
                site.IsChecked = false;
        }

        private void OnSiteCheckedChanged(object? sender, EventArgs e)
        {
            // 勾选即生效
            DispatcherQueue.TryEnqueue(() =>
            {
                SyncBlacklist();
                RefreshPreview();
                SaveSettings();
            });
        }

        // ==================== 开关屏蔽 ====================

        private void OnBlockToggled()
        {
            if (_loadingSettings || _suppressToggle)
            {
                UpdateChrome();
                return;
            }

            if (BlockToggle.IsOn) EnableBlocking(silent: false);
            else DisableBlocking();

            UpdateChrome();
        }

        private void EnableBlocking(bool silent)
        {
            try
            {
                // 端口 53 可能被别的 DNS 服务占着，先腾出来
                var (killed, skipped) = PortHelper.ReleasePort(_dnsServer.Port);
                if (killed.Count > 0)
                {
                    SafeLog($"已结束占用 53 端口的进程: {string.Join(", ", killed)}");
                    Thread.Sleep(600);
                }
                if (skipped.Count > 0)
                    SafeLog($"警告：跳过占用 53 端口的系统进程: {string.Join(", ", skipped)}");

                var remain = PortHelper.FindProcessesUsingPort(_dnsServer.Port);
                if (remain.Count > 0)
                {
                    var names = string.Join(", ", remain.Select(x => $"{x.Name}(PID:{x.Pid})"));
                    SetBlockToggleSilently(false);
                    SafeLog($"失败：端口 53 仍被占用，无法启用屏蔽：{names}");
                    _ = ShowErrorAsync($"端口 53 仍被占用，无法启用屏蔽：{names}");
                    return;
                }

                _dnsServer.UpdateBlacklist(GetEffectiveBlacklist());
                _dnsServer.Start();
                DnsConfigurator.SetDnsToLocalhost();
                DnsConfigurator.FlushCache();

                if (!BlockToggle.IsOn) SetBlockToggleSilently(true);

                SafeLog("屏蔽已启用：本地 DNS 127.0.0.1:53 已启动，系统 DNS 已接管");
                SetStatus("屏蔽已启用。");

                // 没关掉浏览器的加密 DNS，屏蔽随时可能被绕过，这里必须提醒
                if (!DohToggle.IsOn)
                    SafeLog("提示：未阻止浏览器加密 DNS，浏览器开着「安全 DNS」时会绕过屏蔽");

                if (!silent) SaveSettings();
            }
            catch (Exception ex)
            {
                try { _dnsServer.Stop(); } catch { }
                try { DnsConfigurator.RestoreDhcpDns(); } catch { }
                SetBlockToggleSilently(false);
                _ = ShowErrorAsync("启用屏蔽失败：" + ex.Message);
            }

            UpdateChrome();
        }

        private void DisableBlocking()
        {
            try
            {
                _dnsServer.Stop();
                DnsConfigurator.RestoreDhcpDns();
                DnsConfigurator.FlushCache();

                if (BlockToggle.IsOn) SetBlockToggleSilently(false);

                SafeLog("屏蔽已关闭，系统 DNS 已还原为 DHCP");
                SetStatus("屏蔽已关闭。");
                SaveSettings();
            }
            catch (Exception ex)
            {
                _ = ShowErrorAsync("关闭屏蔽失败：" + ex.Message);
            }

            UpdateChrome();
        }

        private void SetBlockToggleSilently(bool value)
        {
            _suppressToggle = true;
            BlockToggle.IsOn = value;
            _suppressToggle = false;
        }

        // ==================== 浏览器加密 DNS（DoH） ====================

        private async Task OnDohToggledAsync()
        {
            if (_loadingSettings || _suppressToggle) return;

            try
            {
                if (DohToggle.IsOn)
                {
                    var results = BrowserDohGuard.Apply();
                    int ok = results.Count(x => x.StartsWith("✔"));

                    SafeLog($"已关闭浏览器「安全 DNS」（{ok}/{results.Count} 个），" +
                            $"并拦截 {BrowserDohGuard.DohDomains.Length} 个 DoH 域名");

                    // 只把出问题的那些列出来，正常的不刷屏
                    foreach (var bad in results.Where(x => x.StartsWith("✖")))
                        SafeLog("   " + bad);

                    SafeLog("   浏览器重启后生效");
                }
                else
                {
                    var results = BrowserDohGuard.Remove();
                    SafeLog("已移除浏览器「安全 DNS」策略");

                    foreach (var bad in results.Where(x => x.StartsWith("✖")))
                        SafeLog("   " + bad);
                }

                SyncBlacklist();
                RefreshPreview();
                SaveSettings();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("设置浏览器 DoH 策略失败：" + ex.Message);
            }

            await Task.CompletedTask;
        }

        // ==================== 一键解除 ====================

        private async Task ClearAllAsync()
        {
            bool yes = await MessageWindow.ConfirmAsync(this,
                "全部解除",
                "确定要解除全部屏蔽吗？将执行：\n\n" +
                "· 取消全部网站的勾选\n" +
                "· 停止本地 DNS 服务\n" +
                "· 还原系统 DNS 为自动获取（DHCP）\n" +
                "· 移除浏览器「安全 DNS / DoH」策略\n" +
                "· 清理旧版本遗留的 hosts 屏蔽块与防火墙规则\n\n" +
                "未勾选任何网站时，屏蔽自然失效。",
                primaryText: "全部解除", secondaryText: "取消",
                kind: MessageKind.Warning, wide: true);

            if (!yes) return;

            try
            {
                SafeLog("正在解除全部屏蔽...");

                SelectNone();

                DisableBlocking();

                // 浏览器 DoH 策略也要还原，否则会留下一个没有 UI 入口可关的系统改动
                if (DohToggle.IsOn)
                {
                    SetDohToggleSilently(false);
                    try
                    {
                        foreach (var line in BrowserDohGuard.Remove()) SafeLog("   " + line);
                        SafeLog("浏览器 DoH 策略已移除（浏览器重启后生效）");
                    }
                    catch (Exception ex)
                    {
                        SafeLog("警告：移除浏览器 DoH 策略失败: " + ex.Message);
                    }
                }

                PurgeLegacyHosts();
                await Task.Run(PurgeLegacyFirewallRules);

                RefreshPreview();
                SaveSettings();
                SetStatus("已解除全部屏蔽。");
                SafeLog("全部解除完成");
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("操作失败：" + ex.Message);
            }
        }

        private void SetDohToggleSilently(bool value)
        {
            _suppressToggle = true;
            DohToggle.IsOn = value;
            _suppressToggle = false;
        }

        // ==================== 旧版本遗留清理 ====================

        private void PurgeLegacyHosts()
        {
            try
            {
                if (!LegacyHostsCleaner.HasLegacyBlock()) return;

                int removed = LegacyHostsCleaner.Purge();
                if (removed > 0)
                    SafeLog($"已清除旧版本遗留的 hosts 屏蔽块（{removed} 行）");
                else if (removed == LegacyHostsCleaner.ResultRefusedUnbalanced)
                    SafeLog("警告：hosts 中的 Class Firewall 标记不成对，已放弃清理以免误删你的内容");
                else
                    SafeLog("警告：检测到 hosts 遗留屏蔽块，但清理失败（可能无权限）");
            }
            catch (Exception ex)
            {
                SafeLog("警告：清理 hosts 遗留块失败: " + ex.Message);
            }
        }

        private void PurgeLegacyFirewallRules()
        {
            try
            {
                int removed = LegacyFirewallCleaner.Purge(out string detail);
                if (removed > 0)
                    SafeLog($"已清除旧版本遗留的防火墙规则 {removed} 条");
                else if (removed == LegacyFirewallCleaner.Failed)
                    SafeLog("警告：清理旧版本防火墙规则失败: " + detail);
            }
            catch (Exception ex)
            {
                SafeLog("警告：清理防火墙遗留规则失败: " + ex.Message);
            }
        }

        // ==================== 开机自启 ====================

        private async void OnAutoStartToggled()
        {
            if (_loadingSettings || _suppressToggle) return;

            try
            {
                if (AutoStartToggle.IsOn)
                {
                    if (!AutoStartManager.Enable(out string detail))
                    {
                        SetAutoStartSilently(false);
                        SafeLog("失败：创建开机自启任务失败：" + detail);
                        await ShowErrorAsync("设置开机自启失败。\n\n" + detail);
                        return;
                    }

                    SafeLog("已添加开机自启任务");

                    // 没勾「启动时自动恢复屏蔽」的话，开机拉起来的只是个空壳
                    if (!AutoBlockToggle.IsOn)
                        SafeLog("提示：「启动时自动恢复上次的屏蔽」未勾选，开机后不会启用屏蔽，建议一起勾上");
                }
                else
                {
                    AutoStartManager.Disable(out _);
                    SafeLog("已移除开机自启任务");
                }

                SaveSettings();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("操作开机自启失败：" + ex.Message);
            }
        }

        private void SetAutoStartSilently(bool value)
        {
            _suppressToggle = true;
            AutoStartToggle.IsOn = value;
            _suppressToggle = false;
        }

        /// <summary>
        /// 让开关与启动项的真实状态保持一致。
        /// 设置里开着自启时，顺手用当前 exe 覆盖写一次，程序被移动过也能自愈。
        /// </summary>
        private void ReconcileAutoStartToggle()
        {
            try
            {
                bool real = AutoStartManager.IsEnabled(out string detail);

                if (_settings.AutoStart)
                {
                    if (!real)
                    {
                        if (AutoStartManager.Enable(out string enableDetail))
                            SafeLog("开机自启任务缺失，已自动补建");
                        else
                        {
                            SafeLog("失败：开机自启任务缺失且补建失败：" + enableDetail);
                            SetAutoStartSilently(false);
                            SaveSettings();
                            return;
                        }
                    }
                    else
                    {
                        AutoStartManager.Enable(out _);
                    }

                    SetAutoStartSilently(true);
                    return;
                }

                // 设置里没开：以真实状态为准
                if (AutoStartToggle.IsOn == real) return;

                SetAutoStartSilently(real);
                SafeLog(real
                    ? "检测到开机自启任务已存在，开关已同步为开启"
                    : "未找到开机自启任务，开关已同步为关闭" +
                      (detail.Length > 0 ? $"（{detail}）" : ""));
            }
            catch { }
        }

        // ==================== 设置持久化 ====================

        private void SaveSettings()
        {
            if (_loadingSettings) return;

            SettingsStore.Save(new AppSettings
            {
                CheckedSites = _vm.CheckedSites.Select(x => x.Name).ToList(),
                DnsEnabled = BlockToggle.IsOn,
                AutoStart = AutoStartToggle.IsOn,
                AutoBlock = AutoBlockToggle.IsOn,
                BlockDoh = DohToggle.IsOn,
                PasswordHash = _passwordHash,
                Theme = _settings.Theme
            });
        }

        // ==================== 界面刷新 ====================

        private void UpdateChrome()
        {
            bool on = BlockToggle.IsOn;

            TitleBarBadge.Background = (Brush)Application.Current.Resources[
                on ? "SystemFillColorSuccessBackgroundBrush" : "SystemFillColorCriticalBackgroundBrush"];
            TitleBarBadgeDot.Fill = (Brush)Application.Current.Resources[
                on ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"];
            TitleBarBadgeText.Foreground = (Brush)Application.Current.Resources[
                on ? "SystemFillColorSuccessBrush" : "SystemFillColorCriticalBrush"];
            TitleBarBadgeText.Text = on ? "屏蔽中" : "未启用";
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private void RefreshPreview()
        {
            var blocked = _vm.SelectedDomains();

            PreviewLabel.Text = blocked.Count == 0
                ? "当前屏蔽的域名"
                : $"当前屏蔽的域名（{blocked.Count} 个）";
            PreviewCount.Text = $"{blocked.Count} 个";

            PreviewText.Text = blocked.Count == 0
                ? "(还没有勾选任何网站)"
                : string.Join(Environment.NewLine, blocked);

            SiteCountText.Text = $"共 {_vm.Sites.Count} 个站点，已选 {_vm.CheckedCount} 个";

            if (DohToggle.IsOn)
            {
                DohHintText.Text = $"另含 {BrowserDohGuard.DohDomains.Length} 个 DoH 服务商域名（见运行日志）";
                DohHintText.Visibility = Visibility.Visible;
            }
            else
            {
                DohHintText.Visibility = Visibility.Collapsed;
            }
        }

        private void SyncBlacklist()
        {
            if (_dnsServer.IsRunning)
                _dnsServer.UpdateBlacklist(GetEffectiveBlacklist());

            SafeLog($"黑名单已更新，共 {GetEffectiveBlacklist().Distinct().Count()} 个域名");
        }

        /// <summary>实际下发的黑名单 = 勾选站点的域名 + （可选）DoH 服务商域名</summary>
        private List<string> GetEffectiveBlacklist()
        {
            var list = _vm.SelectedDomains();
            if (DohToggle.IsOn) list.AddRange(BrowserDohGuard.DohDomains);
            return list;
        }

        private void SafeLog(string msg)
        {
            if (DispatcherQueue == null) return;

            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => SafeLog(msg));
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logLines.Add(line);

            // 只留最近 500 条，避免长时间运行内存一直涨
            while (_logLines.Count > 500) _logLines.RemoveAt(0);

            try { LogList.ScrollIntoView(line); } catch { }
        }

        private async Task ShowErrorAsync(string msg) =>
            await MessageWindow.ShowAsync(this, AppTitle, msg, "确定", MessageKind.Error);

        // ==================== Win32 辅助 ====================

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
