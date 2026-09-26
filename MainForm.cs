using System;
using System.Collections.Generic;
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
            _loadingSettings = false;

            // 事件
            _clearBtn.Click += (_, __) => ClearAll();
            _flushBtn.Click += (_, __) =>
            {
                DnsConfigurator.FlushCache();
                SafeLog("🧹 已刷新 DNS 缓存");
                SetStatus("DNS 缓存已刷新。");
            };
            _blockToggle.CheckedChanged += (_, __) => ToggleBlocking();
            _autoStartToggle.CheckedChanged += (_, __) => OnAutoStartChanged();
            _autoBlockToggle.CheckedChanged += (_, __) => SaveSettings();

            // 勾选即生效
            _siteList.ItemCheck += (_, __) => BeginInvoke(new Action(() =>
            {
                SyncBlacklist();
                RefreshPreview();
                SaveSettings();
            }));

            _dnsServer.OnBlocked += d => SafeLog($"🛡 已拦截: {d}");
            _dnsServer.OnError += msg => SafeLog($"⚠ DNS: {msg}");

            FormClosing += (_, __) =>
            {
                SaveSettings();

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

                if (_settings.AutoBlock && _settings.DnsEnabled)
                    EnableBlocking(silent: false);
            };
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

                _dnsServer.UpdateBlacklist(GetSelectedDomains());
                _dnsServer.Start();
                DnsConfigurator.SetDnsToLocalhost();
                DnsConfigurator.FlushCache();

                if (!_blockToggle.Checked) SetToggleSilently(true);

                SafeLog("▶ 屏蔽已启用：本地 DNS 127.0.0.1:53 已启动，系统 DNS 已接管");
                SetStatus("屏蔽已启用。");

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

        // ---------------- 一键解除 ----------------

        private void ClearAll()
        {
            var r = MessageBox.Show(this,
                "确定要解除全部屏蔽吗？将执行：\n\n" +
                "• 取消全部网站的勾选\n" +
                "• 停止本地 DNS 服务\n" +
                "• 还原系统 DNS 为自动获取（DHCP）\n" +
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
                    if (!AutoStartManager.Enable())
                    {
                        SetAutoStartSilently(false);
                        ShowError("设置开机自启失败。\r\n可能原因：系统策略限制、任务计划程序服务被禁用。");
                        return;
                    }
                    SafeLog("✅ 已添加开机自启任务（最高权限运行，免 UAC）");
                }
                else
                {
                    AutoStartManager.Disable();
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

        private void ReconcileAutoStartToggle()
        {
            try
            {
                bool real = AutoStartManager.IsEnabled();
                if (_autoStartToggle.Checked == real) return;

                SetAutoStartSilently(real);
                SafeLog(real
                    ? "ℹ 检测到开机自启任务已存在，开关已同步为开启"
                    : "ℹ 未找到开机自启任务，开关已同步为关闭");
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
                AutoBlock = _autoBlockToggle.Checked
            });
        }

        // ---------------- 通用 ----------------

        private void SyncBlacklist()
        {
            if (_dnsServer.IsRunning)
                _dnsServer.UpdateBlacklist(GetSelectedDomains());

            SafeLog($"📋 黑名单已更新，共 {GetSelectedDomains().Distinct().Count()} 个域名");
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

            _preview.Text = blocked.Count == 0
                ? "(还没有勾选任何网站)"
                : string.Join(Environment.NewLine, blocked);
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
