using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ClassFirewall
{
    /// <summary>
    /// 主窗体：一个只用本地 DNS 的网站屏蔽工具。
    ///
    /// 屏蔽原理（只有一层，尽量简单可靠）：
    ///   1. 本机监听 127.0.0.1:53 当 DNS 服务器
    ///   2. 把系统网卡 DNS 指向 127.0.0.1
    ///   3. 命中黑名单的域名一律解析到 127.0.0.1 → 网站打不开
    ///   4. 未命中的查询转发上游，正常上网不受影响
    ///
    /// 已下线的层（代码在 archive/ 里）：DPI/SNI 深度包检查、
    /// MITM 根证书 + 403 拦截页、TUN 模式、防火墙 IP 阻断。
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly DnsServer _dnsServer = new DnsServer(53);
        private readonly AppSettings _settings = SettingsStore.Load();

        private bool _loadingSettings;

        /// <summary>程序化改动开关状态时置位，用于抑制 CheckedChanged 的副作用</summary>
        private bool _suppressToggle;

        // ---- 托盘 ----
        private NotifyIcon _tray = null!;
        private bool _trayHintShown;

        /// <summary>
        /// 正在执行"缩到托盘"这个动作，用于防止 Resize 重入。
        ///
        /// 注意：真正导致栈溢出（0xC00000FD）的不是缺这个标志，而是
        /// `MinimizeToTray` 里改 `ShowInTaskbar`（会重建窗口句柄，且重建发生在
        /// WmCreate 内部，标志来不及生效）。那个写法已经彻底移除。
        /// 这个标志保留作为额外的防御。
        /// </summary>
        private bool _trayTransition;

        /// <summary>窗口初始化完成前不做任何托盘操作（Load 结束时才置 true）</summary>
        private bool _formReady;

        /// <summary>true 表示确实要退出，而不是关窗口缩到托盘</summary>
        private bool _reallyExit;

        // ---- 密码 ----
        private string _passwordHash = "";

        public MainForm()
        {
            InitializeComponent();

            foreach (var s in SiteCatalog.Sites)
                _siteList.Items.Add(s);

            // 恢复上次配置
            _loadingSettings = true;
            RestoreCheckedSites();
            _blockToggle.Checked = _settings.DnsEnabled;
            _autoStartToggle.Checked = _settings.AutoStart;
            _autoBlockToggle.Checked = _settings.AutoBlock;
            _dohToggle.Checked = _settings.BlockDoh;
            _passwordHash = _settings.PasswordHash;
            _loadingSettings = false;

            SetupTray();

            // 「设置密码」按钮（动态添加，不占 Designer）
            var passwordBtn = new Button
            {
                Text = "设置密码",
                AutoSize = true,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 0, 8, 0)
            };
            passwordBtn.Click += (_, __) => ChangePassword();
            _buttonsPanel.Controls.Add(passwordBtn);

            // 事件
            _clearBtn.Click += (_, __) => ClearAll();            _flushBtn.Click += (_, __) =>
            {
                DnsConfigurator.FlushCache();
                SafeLog("🧹 已刷新 DNS 缓存");
                SetStatus("DNS 缓存已刷新。");
            };
            _blockToggle.CheckedChanged += (_, __) => ToggleBlocking();
            _autoStartToggle.CheckedChanged += (_, __) => OnAutoStartChanged();
            _autoBlockToggle.CheckedChanged += (_, __) => SaveSettings();
            _dohToggle.CheckedChanged += (_, __) => OnDohToggled();

            // 勾选即生效
            _siteList.ItemCheck += (_, __) => BeginInvoke(new Action(() =>
            {
                SyncBlacklist();
                RefreshPreview();
                SaveSettings();
            }));

            _dnsServer.OnBlocked += d => SafeLog($"🛡 已拦截: {d}");
            _dnsServer.OnError += msg => SafeLog($"⚠ DNS: {msg}");

            // 最小化 → 缩到托盘，不占任务栏
            Resize += (_, __) =>
            {
                // 初始化完成前不动作；正在缩托盘时不重入。
                // （真正致命的不是重入，而是 MinimizeToTray 里改 ShowInTaskbar
                //   导致句柄重建 —— 那个已经彻底去掉了，见 MinimizeToTray 的注释。）
                if (!_formReady || _trayTransition) return;
                if (WindowState == FormWindowState.Minimized) MinimizeToTray();
            };

            FormClosing += (_, e) =>
            {
                // 设了密码时，点关闭按钮不退出，而是缩到托盘继续屏蔽 ——
                // 否则使用者一点叉号就把屏蔽关掉了，密码就白设了
                if (!_reallyExit && PasswordGate.IsConfigured(_passwordHash))
                {
                    e.Cancel = true;
                    MinimizeToTray();
                    ShowTrayHint("程序仍在后台屏蔽。要退出请右键托盘图标并输入密码。");
                    return;
                }

                SaveSettings();
                DisposeTray();

                // 只要接管过系统 DNS 就必须还原，否则关掉程序就断网
                if (_blockToggle.Checked || _dnsServer.IsRunning)
                {
                    try { _dnsServer.Stop(); } catch { }
                    try { DnsConfigurator.RestoreDhcpDns(); } catch { }
                    try { DnsConfigurator.FlushCache(); } catch { }
                }
            };

            Load += async (_, __) =>
            {
                // 升级清理：旧版本遗留的 hosts 屏蔽块与防火墙规则
                PurgeLegacyHosts();
                await System.Threading.Tasks.Task.Run(() => PurgeLegacyFirewallRules());

                // 开机自启开关以任务计划程序中的真实状态为准
                ReconcileAutoStartToggle();

                RefreshPreview();

                bool silent = Program.SilentMode;

                if (silent)
                {
                    // ★ 不在 Load 里直接缩托盘：此刻窗口正在创建/首次布局，
                    //   这时候改 ShowInTaskbar 会触发句柄重建 → Resize 重入 → 栈溢出。
                    //   推迟到消息循环下一轮（窗口已就绪）再执行。
                    BeginInvoke(new Action(() =>
                    {
                        _formReady = true;
                        MinimizeToTray();
                    }));
                }
                else
                {
                    _formReady = true;

                    if (!PasswordGate.IsConfigured(_passwordHash))
                        MaybeOfferPasswordSetup();      // 首次运行：提示设置密码
                }

                LogLockState();

                // ★ 静默启动也必须真正恢复屏蔽 —— 开机自启的意义就在这里。
                //   这里曾经提前 return，结果是程序起来了却什么都没做，
                //   表现就是"开机自启没有用"。
                if (_settings.AutoBlock && _settings.DnsEnabled)
                    EnableBlocking(silent: silent);
            };
        }

        // ---------------- 托盘 ----------------

        private void SetupTray()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("打开主界面");
            openItem.Click += (_, __) => RestoreFromTray();

            var exitItem = new ToolStripMenuItem("退出程序");
            exitItem.Click += (_, __) => TryExitFromTray();

            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "Class Firewall — 网站屏蔽",
                ContextMenuStrip = menu,
                Visible = false
            };

            // 双击托盘图标也能打开（同样要过密码）
            _tray.DoubleClick += (_, __) => RestoreFromTray();
        }

        private void DisposeTray()
        {
            if (_tray == null) return;
            _tray.Visible = false;
            _tray.Dispose();
        }

        /// <summary>
        /// 缩到系统托盘。
        ///
        /// ★★ 绝对不要在这里改 `ShowInTaskbar` —— 这是实测出来的：
        ///   `Form.ShowInTaskbar` 一旦变化，WinForms 会**重建窗口句柄**，
        ///   而重建过程发生在 `WmCreate` 内部，会在窗口创建途中再次触发 `Resize`；
        ///   `Resize` 又调本方法 …… 形成无限递归，最终**栈溢出（0xC00000FD）**，
        ///   进程被直接终止，连异常都捕获不到，界面上毫无提示。
        ///   实测：加防重入标志也**挡不住**（重建发生在标志生效之前）。
        ///   唯一可靠的写法就是只 `Hide()`。
        ///
        ///   而且根本不需要它：**隐藏的窗口本来就不会显示任务栏按钮**。
        /// </summary>
        private void MinimizeToTray()
        {
            if (_trayTransition) return;
            if (_tray == null) return;
            if (!Visible && _tray.Visible) return;   // 已经在托盘里了

            _trayTransition = true;
            try
            {
                if (Visible) Hide();
                _tray.Visible = true;
            }
            finally
            {
                _trayTransition = false;
            }
        }

        private void ShowTrayHint(string text)
        {
            if (_trayHintShown || _tray == null) return;
            _trayHintShown = true;

            try
            {
                _tray.BalloonTipTitle = "Class Firewall";
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            // 打开界面要过密码，否则设了密码也白设
            if (!RequirePassword("打开主界面")) return;
            if (_trayTransition) return;

            _trayTransition = true;
            try
            {
                // 同样不碰 ShowInTaskbar（见 MinimizeToTray 的说明）：
                // 窗口重新显示时任务栏按钮会自然回来
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                if (!Visible) Show();
                _tray.Visible = false;
                Activate();
            }
            finally
            {
                _trayTransition = false;
            }
        }

        private void TryExitFromTray()
        {
            if (!RequirePassword("退出程序")) return;

            _reallyExit = true;
            Close();
        }

        // ---------------- 密码 ----------------

        /// <summary>要求输入密码；没设密码时直接放行。万能密码一律通过</summary>
        private bool RequirePassword(string action)
        {
            if (!PasswordGate.IsConfigured(_passwordHash)) return true;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                string prompt = attempt == 1
                    ? $"「{action}」需要密码，请输入："
                    : $"密码不对，再试一次（第 {attempt}/3 次）：";

                string? input = PasswordDialog.Verify(this, "需要密码", prompt);
                if (input == null) return false;
                if (PasswordGate.Verify(input, _passwordHash)) return true;
            }

            SafeLog("⚠ 密码连续输错 3 次，操作已取消");
            return false;
        }

        private void LogLockState()
        {
            SafeLog(PasswordGate.IsConfigured(_passwordHash)
                ? "🔒 已启用密码保护"
                : "🔓 未设置密码");
        }

        /// <summary>
        /// 首次运行（还没设密码）时提示设置一次。
        /// 只是"提示"，用户可以直接跳过 —— 不该拦着人用工具。
        /// </summary>
        private void MaybeOfferPasswordSetup()
        {
            var r = MessageBox.Show(this,
                "要不要给本程序设一个密码？\n\n" +
                "• 设了之后，打开界面、退出程序都需要输入密码\n" +
                "• 这样使用者就没法自己把屏蔽关掉\n" +
                "• 忘记密码可以用万能密码解锁（见 README）\n" +
                "• 之后也可以随时点「设置密码」修改\n\n" +
                "现在设置密码吗？",
                "设置密码",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (r == DialogResult.Yes) ChangePassword();
        }

        /// <summary>设置 / 修改 / 取消密码。已有密码时先要求验证原密码</summary>
        private void ChangePassword()
        {
            try
            {
                // 已经设过密码：必须先证明自己是管理员
                if (PasswordGate.IsConfigured(_passwordHash) &&
                    !RequirePassword("修改密码"))
                    return;

                string prompt = PasswordGate.IsConfigured(_passwordHash)
                    ? "输入新密码。\r\n（留空并确定 = 取消密码保护）"
                    : "设置一个密码，之后打开本程序需要输入它才能解锁。\r\n（留空并确定 = 不设密码）";

                if (!PasswordDialog.SetNew(this, out string newPassword, prompt)) return;

                if (newPassword.Length == 0)
                {
                    _passwordHash = "";
                    SaveSettings();
                    SafeLog("🔓 已取消密码保护");
                    SetStatus("密码保护已取消。");
                }
                else
                {
                    _passwordHash = PasswordGate.CreateHash(newPassword);
                    SaveSettings();
                    SafeLog("🔒 密码已设置：打开界面与退出都需要密码");
                    SetStatus("密码已设置。");
                }
            }
            catch (Exception ex)
            {
                ShowError("设置密码失败：" + ex.Message);
            }
        }

        // ---------------- 开关屏蔽 ----------------

        private void ToggleBlocking()
        {
            if (_loadingSettings || _suppressToggle) return;

            if (_blockToggle.Checked) EnableBlocking(silent: false);
            else DisableBlocking();
        }

        private void EnableBlocking(bool silent)
        {
            try
            {
                // 端口 53 可能被别的 DNS 服务占着，先腾出来
                var (killed, skipped) = PortHelper.ReleasePort(_dnsServer.Port);
                if (killed.Count > 0)
                {
                    SafeLog($"🔧 已结束占用 53 端口的进程: {string.Join(", ", killed)}");
                    Thread.Sleep(600);
                }
                if (skipped.Count > 0)
                    SafeLog($"⚠ 跳过占用 53 端口的系统进程: {string.Join(", ", skipped)}");

                var remain = PortHelper.FindProcessesUsingPort(_dnsServer.Port);
                if (remain.Count > 0)
                {
                    var names = string.Join(", ", remain.Select(x => $"{x.Name}(PID:{x.Pid})"));
                    SetToggleSilently(false);
                    ShowError($"端口 53 仍被占用，无法启用屏蔽：{names}");
                    return;
                }

                _dnsServer.UpdateBlacklist(GetEffectiveBlacklist());
                _dnsServer.Start();
                DnsConfigurator.SetDnsToLocalhost();
                DnsConfigurator.FlushCache();

                if (!_blockToggle.Checked) SetToggleSilently(true);

                SafeLog("▶ 屏蔽已启用：本地 DNS 127.0.0.1:53 已启动，系统 DNS 已接管");
                SetStatus("屏蔽已启用。");

                // 没关掉浏览器的加密 DNS，屏蔽随时可能被绕过，这里必须提醒
                if (!_dohToggle.Checked)
                {
                    SafeLog("⚠ 未阻止浏览器加密 DNS，浏览器开着「安全 DNS」时会绕过屏蔽");
                }

                if (!silent) SaveSettings();
            }
            catch (Exception ex)
            {
                try { _dnsServer.Stop(); } catch { }
                try { DnsConfigurator.RestoreDhcpDns(); } catch { }
                SetToggleSilently(false);
                ShowError("启用屏蔽失败：" + ex.Message);
            }
        }

        private void DisableBlocking()
        {
            try
            {
                _dnsServer.Stop();
                DnsConfigurator.RestoreDhcpDns();
                DnsConfigurator.FlushCache();

                if (_blockToggle.Checked) SetToggleSilently(false);

                SafeLog("■ 屏蔽已关闭，系统 DNS 已还原为 DHCP");
                SetStatus("屏蔽已关闭。");
                SaveSettings();
            }
            catch (Exception ex)
            {
                ShowError("关闭屏蔽失败：" + ex.Message);
            }
        }

        private void SetToggleSilently(bool value)
        {
            _suppressToggle = true;
            _blockToggle.Checked = value;
            _suppressToggle = false;
        }

        // ---------------- 浏览器加密 DNS（DoH） ----------------

        private void SetDohSilently(bool value)
        {
            _suppressToggle = true;
            _dohToggle.Checked = value;
            _suppressToggle = false;
        }

        /// <summary>
        /// 勾选：写浏览器企业策略关掉「安全 DNS」，并把常见 DoH 域名一起黑洞掉。
        /// 取消：把策略还原（只删本程序写的值）。
        /// </summary>
        private void OnDohToggled()
        {
            if (_loadingSettings || _suppressToggle) return;

            try
            {
                if (_dohToggle.Checked)
                {
                    var results = BrowserDohGuard.Apply();
                    int ok = results.Count(x => x.StartsWith("✔"));

                    SafeLog($"🔒 已关闭浏览器「安全 DNS」（{ok}/{results.Count} 个），" +
                            $"并拦截 {BrowserDohGuard.DohDomains.Length} 个 DoH 域名");

                    // 只把出问题的那些列出来，正常的不刷屏
                    foreach (var bad in results.Where(x => x.StartsWith("✖")))
                        SafeLog("   " + bad);

                    SafeLog("   浏览器重启后生效");
                }
                else
                {
                    var results = BrowserDohGuard.Remove();
                    SafeLog("🔓 已移除浏览器「安全 DNS」策略");

                    foreach (var bad in results.Where(x => x.StartsWith("✖")))
                        SafeLog("   " + bad);
                }

                SyncBlacklist();
                RefreshPreview();
                SaveSettings();
            }
            catch (Exception ex)
            {
                ShowError("设置浏览器 DoH 策略失败：" + ex.Message);
            }
        }

        // ---------------- 一键解除 ----------------

        private void ClearAll()
        {
            var r = MessageBox.Show(this,
                "确定要解除全部屏蔽吗？将执行：\n\n" +
                "• 取消全部网站的勾选\n" +
                "• 停止本地 DNS 服务\n" +
                "• 还原系统 DNS 为自动获取（DHCP）\n" +
                "• 移除浏览器「安全 DNS / DoH」策略\n" +
                "• 清理旧版本遗留的 hosts 屏蔽块与防火墙规则\n\n" +
                "未勾选任何网站时，屏蔽自然失效。",
                "全部解除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (r != DialogResult.Yes) return;

            try
            {
                SafeLog("🧨 正在解除全部屏蔽...");

                _loadingSettings = true;
                for (int i = 0; i < _siteList.Items.Count; i++)
                    _siteList.SetItemChecked(i, false);
                _loadingSettings = false;

                DisableBlocking();

                // 浏览器 DoH 策略也要还原，否则会留下一个没有 UI 入口可关的系统改动
                if (_dohToggle.Checked)
                {
                    SetDohSilently(false);
                    try
                    {
                        foreach (var line in BrowserDohGuard.Remove()) SafeLog("   " + line);
                        SafeLog("🔓 浏览器 DoH 策略已移除（浏览器重启后生效）");
                    }
                    catch (Exception ex)
                    {
                        SafeLog("⚠ 移除浏览器 DoH 策略失败: " + ex.Message);
                    }
                }

                PurgeLegacyHosts();
                PurgeLegacyFirewallRules();

                RefreshPreview();
                SaveSettings();
                SetStatus("已解除全部屏蔽。");
                SafeLog("✅ 全部解除完成");
            }
            catch (Exception ex)
            {
                ShowError("操作失败：" + ex.Message);
            }
        }

        // ---------------- 旧版本遗留清理 ----------------

        private void PurgeLegacyHosts()
        {
            try
            {
                if (!LegacyHostsCleaner.HasLegacyBlock()) return;

                int removed = LegacyHostsCleaner.Purge();
                if (removed > 0)
                    SafeLog($"🧹 已清除旧版本遗留的 hosts 屏蔽块（{removed} 行）");
                else if (removed == LegacyHostsCleaner.ResultRefusedUnbalanced)
                    SafeLog("⚠ hosts 中的 Class Firewall 标记不成对，已放弃清理以免误删你的内容");
                else
                    SafeLog("⚠ 检测到 hosts 遗留屏蔽块，但清理失败（可能无权限）");
            }
            catch (Exception ex)
            {
                SafeLog("⚠ 清理 hosts 遗留块失败: " + ex.Message);
            }
        }

        private void PurgeLegacyFirewallRules()
        {
            try
            {
                int removed = LegacyFirewallCleaner.Purge(out string detail);
                if (removed > 0)
                    SafeLog($"🧹 已清除旧版本遗留的防火墙规则 {removed} 条");
                else if (removed == LegacyFirewallCleaner.Failed)
                    SafeLog("⚠ 清理旧版本防火墙规则失败: " + detail);
            }
            catch (Exception ex)
            {
                SafeLog("⚠ 清理防火墙遗留规则失败: " + ex.Message);
            }
        }

        // ---------------- 开机自启 ----------------

        private void OnAutoStartChanged()
        {
            if (_loadingSettings || _suppressToggle) return;

            try
            {
                if (_autoStartToggle.Checked)
                {
                    if (!AutoStartManager.Enable(out string detail))
                    {
                        SetAutoStartSilently(false);
                        SafeLog("✖ 创建开机自启任务失败：" + detail);
                        ShowError("设置开机自启失败。\r\n\r\nschtasks 的输出：\r\n" + detail +
                                  "\r\n\r\n常见原因：任务计划程序服务被禁用、系统策略限制。");
                        return;
                    }

                    SafeLog("✅ 已添加开机自启任务");

                    // 光建了任务还不够：没勾「启动时自动恢复屏蔽」的话，
                    // 开机拉起来的只是个空壳，屏蔽照样是关着的 —— 这个坑踩过。
                    if (!_autoBlockToggle.Checked)
                        SafeLog("⚠ 「启动时自动恢复上次的屏蔽」未勾选，开机后不会启用屏蔽，建议一起勾上");
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
                ShowError("操作开机自启失败：" + ex.Message);
            }
        }

        private void SetAutoStartSilently(bool value)
        {
            _suppressToggle = true;
            _autoStartToggle.Checked = value;
            _suppressToggle = false;
        }

        /// <summary>
        /// 让「开机自动启动」开关与任务计划程序里的真实状态保持一致，并做自愈：
        ///
        /// ★ 自愈场景：任务在、但指向的 exe 已经不在原处（程序被移动/改名，
        ///   或曾经从 dist\ 之类临时目录启用过自启）。这种情况下任务照样"存在"，
        ///   但登录时拉不起任何东西 —— 表现就是"开机自启没有用"。
        ///   所以只要设置里开着自启，就顺手用当前 exe 覆盖注册一次
        ///   （schtasks 的 /F 是原子覆盖，不会出现任务真空期）。
        /// </summary>
        private void ReconcileAutoStartToggle()
        {
            try
            {
                bool real = AutoStartManager.IsEnabled(out string detail);

                // 设置里开着自启 —— 用户意图明确，确保任务存在且指向当前 exe
                if (_settings.AutoStart)
                {
                    if (!real)
                    {
                        // 任务不在：这才值得记一笔
                        if (AutoStartManager.Enable(out string enableDetail))
                            SafeLog("ℹ 开机自启任务缺失，已自动补建");
                        else
                        {
                            SafeLog("✖ 开机自启任务缺失且补建失败：" + enableDetail);
                            SetAutoStartSilently(false);
                            SaveSettings();
                            return;
                        }
                    }
                    else
                    {
                        // 已存在：静默刷新一次，保证指向当前 exe（程序被移动过也能自愈）。
                        // 这是每次启动都会做的常规动作，不刷日志。
                        AutoStartManager.Enable(out _);
                    }

                    SetAutoStartSilently(true);
                    SaveSettings();
                    return;
                }

                // 设置里没开：以真实状态为准
                if (_autoStartToggle.Checked == real) return;

                SetAutoStartSilently(real);
                SafeLog(real
                    ? "ℹ 检测到开机自启任务已存在，开关已同步为开启"
                    : "ℹ 未找到开机自启任务，开关已同步为关闭" +
                      (detail.Length > 0 ? $"（{detail}）" : ""));
                SaveSettings();
            }
            catch { }
        }

        // ---------------- 设置 ----------------

        private void RestoreCheckedSites()
        {
            if (_settings.CheckedSites.Count == 0) return;

            for (int i = 0; i < _siteList.Items.Count; i++)
            {
                if (_siteList.Items[i] is not SiteInfo s) continue;
                if (_settings.CheckedSites.Contains(s.Name))
                    _siteList.SetItemChecked(i, true);
            }
        }

        private void SaveSettings()
        {
            if (_loadingSettings) return;

            SettingsStore.Save(new AppSettings
            {
                CheckedSites = _siteList.CheckedItems
                    .OfType<SiteInfo>()
                    .Select(x => x.Name)
                    .ToList(),
                DnsEnabled = _blockToggle.Checked,
                AutoStart = _autoStartToggle.Checked,
                AutoBlock = _autoBlockToggle.Checked,
                BlockDoh = _dohToggle.Checked,
                PasswordHash = _passwordHash
            });
        }

        // ---------------- 通用 ----------------

        private void SyncBlacklist()
        {
            if (_dnsServer.IsRunning)
                _dnsServer.UpdateBlacklist(GetEffectiveBlacklist());

            SafeLog($"📋 黑名单已更新，共 {GetEffectiveBlacklist().Distinct().Count()} 个域名");
        }

        /// <summary>实际下发的黑名单 = 勾选站点的域名 + （可选）DoH 服务商域名</summary>
        private List<string> GetEffectiveBlacklist()
        {
            var list = GetSelectedDomains();
            if (_dohToggle.Checked) list.AddRange(BrowserDohGuard.DohDomains);
            return list;
        }

        private List<string> GetSelectedDomains()
        {
            var list = new List<string>();
            foreach (var item in _siteList.CheckedItems)
                if (item is SiteInfo s) list.AddRange(s.Domains);
            return list;
        }

        private void RefreshPreview()
        {
            var blocked = GetSelectedDomains()
                .Select(d => d.Trim().ToLowerInvariant())
                .Where(d => d.Length > 0 && !d.Contains(' '))
                .Distinct()
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();

            _previewLabel.Text = blocked.Count == 0
                ? "当前屏蔽的域名（暂无）"
                : $"当前屏蔽的域名（{blocked.Count} 个）";

            var text = blocked.Count == 0
                ? "(还没有勾选任何网站)"
                : string.Join(Environment.NewLine, blocked);

            if (_dohToggle.Checked)
                text += Environment.NewLine +
                        $"（另含 {BrowserDohGuard.DohDomains.Length} 个 DoH 服务商域名，见上方日志）";

            _preview.Text = text;
        }

        private void SetStatus(string text) => _status.Text = text;

        private void SafeLog(string msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => SafeLog(msg))); return; }
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }

        private void ShowError(string msg) =>
            MessageBox.Show(this, msg, "Class Firewall",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
