using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassFirewall
{
    public partial class MainForm : Form
    {
        private readonly DeepInspector _inspector = new DeepInspector(80, 443);
        private readonly DnsServer _dnsServer = new DnsServer(53);
        private readonly AppSettings _settings = SettingsStore.Load();

        private bool _loadingSettings;
        private CancellationTokenSource? _fwCts;

        public MainForm()
        {
            InitializeComponent();

            // 填充站点列表
            foreach (var s in SiteCatalog.Sites)
                _siteList.Items.Add(s);

            // 恢复上次勾选状态
            _loadingSettings = true;
            RestoreCheckedSites();
            _autoStartToggle.Checked = _settings.AutoStart;
            _autoBlockToggle.Checked = _settings.AutoBlock;
            _loadingSettings = false;

            // 事件挂接
            _applyBtn.Click += async (_, __) => await ApplySelectionAsync();
            _clearBtn.Click += (_, __) => ClearAll();
            _flushBtn.Click += (_, __) => { HostsManager.FlushDns(); SetStatus("DNS 缓存已刷新。"); };
            _sniToggle.CheckedChanged += (_, __) => ToggleInspector();
            _dnsToggle.CheckedChanged += (_, __) => ToggleDns();
            _autoStartToggle.CheckedChanged += (_, __) => OnAutoStartChanged();
            _autoBlockToggle.CheckedChanged += (_, __) => SaveSettings();

            _siteList.ItemCheck += (_, __) =>
            {
                BeginInvoke(new Action(() =>
                {
                    SyncBlacklistsToRunningServices();
                    SaveSettings();
                }));
            };

            // DPI 事件
            _inspector.OnBlocked += (proto, domain) => SafeLog($"⛔ [{proto}] 已拦截: {domain}");
            _inspector.OnAllowed += (proto, domain) => SafeLog($"✔ [{proto}] 放行: {domain}");
            _inspector.OnError += msg => SafeLog($"⚠ DPI: {msg}");
            _inspector.OnDebug += msg => SafeLog($"🔍 DPI: {msg}");

            // DNS 事件
            _dnsServer.OnBlocked += d => SafeLog($"🛡 DNS 拦截: {d} → 127.0.0.1");
            _dnsServer.OnError += msg => SafeLog($"⚠ DNS: {msg}");

            // 关闭时清理
            FormClosing += (_, __) =>
            {
                SaveSettings();
                _fwCts?.Cancel();
                try { _inspector.Stop(); } catch { }
                if (_dnsServer.IsRunning)
                {
                    try { _dnsServer.Stop(); } catch { }
                    try { DnsConfigurator.RestoreDhcpDns(); } catch { }
                    try { HostsManager.FlushDns(); } catch { }
                }
                // 防火墙规则保留，下次启动时重新同步。彻底解除请点“全部解除”
            };

            Load += async (_, __) =>
            {
                RefreshPreview();

                if (_settings.AutoBlock)
                    await AutoStartBlockAsync();
            };
        }

        // ---------------- 设置持久化 ----------------

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

            var s = new AppSettings
            {
                CheckedSites = _siteList.CheckedItems
                    .OfType<SiteInfo>()
                    .Select(x => x.Name)
                    .ToList(),
                DpiEnabled = _sniToggle.Checked,
                DnsEnabled = _dnsToggle.Checked,
                AutoStart = _autoStartToggle.Checked,
                AutoBlock = _autoBlockToggle.Checked
            };
            SettingsStore.Save(s);
        }

        private void OnAutoStartChanged()
        {
            if (_loadingSettings) return;

            try
            {
                if (_autoStartToggle.Checked)
                {
                    if (AutoStartManager.Enable())
                        SafeLog("✅ 已添加开机自启任务（最高权限运行，免 UAC）");
                    else
                    {
                        _autoStartToggle.Checked = false;
                        ShowError("设置开机自启失败。\r\n" +
                                  "可能原因：系统策略限制、任务计划程序服务被禁用。");
                        return;
                    }
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

        // ---------------- 自动封锁 ----------------

        private async Task AutoStartBlockAsync()
        {
            try
            {
                SafeLog("⏰ 检测到「启动时自动启用屏蔽」，正在恢复...");

                var domains = GetSelectedDomains();
                if (domains.Count == 0)
                {
                    SafeLog("⚠ 没有勾选任何网站，跳过自动封锁");
                    return;
                }

                // 1. hosts
                try
                {
                    HostsManager.Apply(domains);
                    RefreshPreview();
                    SafeLog($"✔ hosts 已恢复，屏蔽 {domains.Distinct().Count()} 个域名");
                }
                catch (Exception ex)
                {
                    SafeLog($"✖ hosts 恢复失败: {ex.Message}");
                }

                // 2. DPI
                if (!_sniToggle.Checked)
                    _sniToggle.Checked = true;

                // 3. DNS
                if (!_dnsToggle.Checked)
                    _dnsToggle.Checked = true;

                // 4. 防火墙 IP 阻断
                await SyncFirewallAsync(domains);

                SetStatus("自动封锁已恢复。");
                SafeLog("✅ 自动封锁完成");
            }
            catch (Exception ex)
            {
                SafeLog($"❌ 自动封锁失败: {ex.Message}");
            }
        }

        // ---------------- 同步 ----------------

        private void SyncBlacklistsToRunningServices()
        {
            var domains = GetSelectedDomains();
            if (_inspector.IsRunning) _inspector.UpdateBlacklist(domains);
            if (_dnsServer.IsRunning) _dnsServer.UpdateBlacklist(domains);
        }

        // ---------------- 端口准备 ----------------

        private bool TryPreparePort(int port, string serviceName)
        {
            var (killed, skipped) = PortHelper.ReleasePort(port);

            if (killed.Count > 0)
            {
                SafeLog($"🔧 已自动结束占用 {port} 端口的进程: {string.Join(", ", killed)}");
                Thread.Sleep(800);
            }

            if (skipped.Count > 0)
                SafeLog($"⚠ 跳过 {port} 端口进程: {string.Join(", ", skipped)}");

            var remain = PortHelper.FindProcessesUsingPort(port);
            if (remain.Count > 0)
            {
                var names = string.Join(", ",
                    remain.Select(x => $"{x.Name}(PID:{x.Pid})"));
                SafeLog($"✖ {serviceName} 无法绑定 {port} 端口，仍被占用: {names}");
                return false;
            }
            return true;
        }

        // ---------------- 深度包检查 ----------------

        private void ToggleInspector()
        {
            try
            {
                if (_sniToggle.Checked)
                {
                    if (!TryPreparePort(_inspector.HttpsPort, "HTTPS 检查"))
                    {
                        _sniToggle.Checked = false;
                        ShowError($"无法释放端口 {_inspector.HttpsPort}。");
                        return;
                    }

                    if (!TryPreparePort(_inspector.HttpPort, "HTTP 检查"))
                    {
                        _sniToggle.Checked = false;
                        ShowError($"无法释放端口 {_inspector.HttpPort}。");
                        return;
                    }

                    _inspector.UpdateBlacklist(GetSelectedDomains());
                    _inspector.Start();
                    SafeLog($"▶ 深度包检查已启动：HTTP {_inspector.HttpPort}, HTTPS {_inspector.HttpsPort}");
                    SetStatus("深度包检查已启用。");
                }
                else
                {
                    _inspector.Stop();
                    SafeLog("■ 深度包检查已停止");
                    SetStatus("深度包检查已关闭。");
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                _sniToggle.Checked = false;
                ShowError("启动深度包检查失败：" + ex.Message);
            }
        }

        // ---------------- DNS ----------------

        private void ToggleDns()
        {
            try
            {
                if (_dnsToggle.Checked)
                {
                    if (!TryPreparePort(_dnsServer.Port, "DNS 服务器"))
                    {
                        _dnsToggle.Checked = false;
                        ShowError($"无法释放端口 {_dnsServer.Port}。");
                        return;
                    }

                    _dnsServer.UpdateBlacklist(GetSelectedDomains());
                    _dnsServer.Start();
                    DnsConfigurator.SetDnsToLocalhost();
                    HostsManager.FlushDns();

                    SafeLog($"▶ DNS 服务器已启动 (127.0.0.1:{_dnsServer.Port})，系统 DNS 已切换");
                    SetStatus("DNS 接管已启用。");
                }
                else
                {
                    _dnsServer.Stop();
                    DnsConfigurator.RestoreDhcpDns();
                    HostsManager.FlushDns();

                    SafeLog("■ DNS 服务器已停止，系统 DNS 已还原");
                    SetStatus("DNS 接管已关闭。");
                }
                SaveSettings();
            }
            catch (Exception ex)
            {
                _dnsToggle.Checked = false;
                ShowError("启动 DNS 服务器失败：" + ex.Message);
            }
        }

        // ---------------- 应用屏蔽（含防火墙同步） ----------------

        private async Task ApplySelectionAsync()
        {
            try
            {
                var domains = GetSelectedDomains();

                HostsManager.Apply(domains);

                if (_inspector.IsRunning) _inspector.UpdateBlacklist(domains);
                if (_dnsServer.IsRunning) _dnsServer.UpdateBlacklist(domains);

                RefreshPreview();
                SaveSettings();
                SetStatus($"已应用 hosts，共 {domains.Distinct().Count()} 个域名。");

                // ★ 同步防火墙：解析真实 IP 后加入出站阻断
                await SyncFirewallAsync(domains);
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("没有权限，请以管理员身份运行。");
            }
            catch (Exception ex)
            {
                ShowError("操作失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 用外部 DNS 解析所有域名的真实 IP，然后同步到 Windows 防火墙。
        /// 这样即使应用绕过了 hosts 和本地 DNS，流量也会被内核层阻断。
        /// </summary>
        private async Task SyncFirewallAsync(List<string> domains)
        {
            _fwCts?.Cancel();
            _fwCts = new CancellationTokenSource();
            var token = _fwCts.Token;

            try
            {
                SafeLog($"🔍 正在用外部 DNS 解析 {domains.Count} 个域名...");

                var allIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int done = 0;

                foreach (var d in domains)
                {
                    if (token.IsCancellationRequested) return;

                    try
                    {
                        var ips = await ExternalDns.ResolveAsync(d, token);
                        foreach (var ip in ips) allIps.Add(ip);
                    }
                    catch { /* 单域名失败继续 */ }

                    done++;
                    if (done % 10 == 0)
                        SafeLog($"   已解析 {done}/{domains.Count}，累计 {allIps.Count} 个 IP");
                }

                if (allIps.Count == 0)
                {
                    SafeLog("⚠ 未能解析出任何 IP，防火墙未更新");
                    return;
                }

                SafeLog($"✅ 共解析出 {allIps.Count} 个唯一 IP，正在写入防火墙规则...");
                FirewallBlocker.Sync(allIps, SafeLog);
                SetStatus($"防火墙已阻断 {allIps.Count} 个 IP");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SafeLog($"⚠ 同步防火墙失败: {ex.Message}");
            }
        }

        // ---------------- 解除 ----------------

        private void ClearAll()
        {
            try
            {
                HostsManager.Clear();
                for (int i = 0; i < _siteList.Items.Count; i++)
                    _siteList.SetItemChecked(i, false);

                if (_inspector.IsRunning) _inspector.UpdateBlacklist(Array.Empty<string>());
                if (_dnsServer.IsRunning) _dnsServer.UpdateBlacklist(Array.Empty<string>());

                _fwCts?.Cancel();

                // ★ 清除防火墙规则
                FirewallBlocker.ClearAll(SafeLog);

                RefreshPreview();
                SaveSettings();
                SetStatus("已解除全部屏蔽规则。");
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("没有权限修改 hosts 文件，请以管理员身份运行本程序。");
            }
            catch (Exception ex)
            {
                ShowError("操作失败：" + ex.Message);
            }
        }

        // ---------------- 通用 ----------------

        private List<string> GetSelectedDomains()
        {
            var list = new List<string>();
            foreach (var item in _siteList.CheckedItems)
                if (item is SiteInfo s) list.AddRange(s.Domains);
            return list;
        }

        private void RefreshPreview()
        {
            var blocked = HostsManager.GetBlockedDomains();
            _preview.Text = blocked.Count == 0
                ? "(当前没有任何屏蔽规则)"
                : string.Join(Environment.NewLine, blocked.Select(d => "127.0.0.1 " + d));
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