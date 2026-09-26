// ==================== BrowserDohGuard.cs ====================
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ClassFirewall
{
    /// <summary>
    /// 阻止浏览器使用加密 DNS（DoH / 安全 DNS）。
    ///
    /// 为什么需要它：浏览器的「安全 DNS」会**绕过系统 DNS**，
    /// 直接向 Cloudflare / Google 等用 HTTPS 发查询。
    /// 我们的屏蔽建立在「把系统 DNS 指向 127.0.0.1」之上，一旦浏览器绕过去，
    /// 命中黑名单的域名就会被正常解析出来，屏蔽直接失效。
    ///
    /// 两道措施：
    ///   ① 写浏览器企业策略，把「安全 DNS」关掉（Chrome / Edge / Brave 官方文档确认；
    ///      Firefox 为官方策略名，但本机环境无法验证注册表路径，属尽力而为）
    ///   ② 把常见 DoH 服务商的域名一起黑洞掉 —— 与浏览器策略无关，
    ///      对「自动」模式下的 DoH 引导解析同样有效
    ///
    /// 策略写在 HKEY_LOCAL_MACHINE 下，改注册表**需要管理员权限**，
    /// 且**需要重启浏览器**才会生效。
    /// </summary>
    internal static class BrowserDohGuard
    {
        /// <summary>
        /// (策略键路径, 值名, 值数据)
        /// 注册表根由调用方给出，正式运行是 LocalMachine，测试可用 CurrentUser。
        /// </summary>
        internal static readonly (string KeyPath, string ValueName, string Value)[] Policies =
        {
            // Chromium 系：DnsOverHttpsMode = off → 禁用 DoH
            // https://learn.microsoft.com/deployedge/microsoft-edge-policies/DnsOverHttpsMode
            (@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off"),
            (@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off"),
            (@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", "off"),

            // Firefox 企业策略：DNSOverHTTPS = {"Enabled": false}
            // 注：策略名取自 Mozilla 官方 policy-templates，注册表路径未能从一手来源核实
            (@"SOFTWARE\Policies\Mozilla\Firefox", "DNSOverHTTPS", "{\"Enabled\": false}"),
        };

        /// <summary>
        /// 常见 DoH 服务商域名。后缀匹配会自动覆盖其子域名
        /// （例如 cloudflare-dns.com 同时覆盖 chrome./mozilla.cloudflare-dns.com）。
        /// </summary>
        public static readonly string[] DohDomains =
        {
            "cloudflare-dns.com",       // Chrome / Firefox 默认 DoH 提供商
            "one.one.one.one",          // Cloudflare 1.1.1.1
            "dns.google",               // Google
            "doh.opendns.com",          // OpenDNS
            "dns.quad9.net",            // Quad9
            "dns.nextdns.io",           // NextDNS
            "dns.adguard.com",          // AdGuard
            "doh.cleanbrowsing.org",    // CleanBrowsing
            "dns.mullvad.net",          // Mullvad
            "doh.pub",                  // 腾讯 DoH
            "dns.alidns.com",           // 阿里 DoH
            "doh.360.cn",               // 360 DoH
        };

        // ---------------- 应用 / 移除 ----------------

        public static List<string> Apply() => Apply(Registry.LocalMachine);

        /// <summary>写入浏览器策略，返回逐条结果说明</summary>
        internal static List<string> Apply(RegistryKey hive)
        {
            var log = new List<string>();

            foreach (var (keyPath, valueName, value) in Policies)
            {
                try
                {
                    using var key = hive.CreateSubKey(keyPath, true);
                    if (key == null)
                    {
                        log.Add($"✖ 打不开策略键 {keyPath}");
                        continue;
                    }

                    key.SetValue(valueName, value, RegistryValueKind.String);
                    log.Add($"✔ {ShortName(keyPath)} ← {valueName}={value}");
                }
                catch (UnauthorizedAccessException)
                {
                    log.Add($"✖ 无权限写入 {keyPath}（需要以管理员身份运行）");
                }
                catch (Exception ex)
                {
                    log.Add($"✖ 写入 {keyPath} 失败: {ex.Message}");
                }
            }

            return log;
        }

        public static List<string> Remove() => Remove(Registry.LocalMachine);

        /// <summary>
        /// 移除我们写入的策略值。
        /// **只删值等于我们写的那个**，别人（或组策略）设的其它值一律不动。
        /// </summary>
        internal static List<string> Remove(RegistryKey hive)
        {
            var log = new List<string>();

            foreach (var (keyPath, valueName, value) in Policies)
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath, true);
                    if (key == null) continue;   // 本来就没有，什么都不用做

                    var current = key.GetValue(valueName) as string;
                    if (current == null) continue;

                    if (!string.Equals(current, value, StringComparison.Ordinal))
                    {
                        log.Add($"⚠ {ShortName(keyPath)} 的 {valueName} 不是本程序写的不删除（当前值 {current}）");
                        continue;
                    }

                    key.DeleteValue(valueName, false);
                    log.Add($"✔ 已移除 {ShortName(keyPath)} 的 {valueName}");
                }
                catch (UnauthorizedAccessException)
                {
                    log.Add($"✖ 无权限移除 {keyPath}（需要以管理员身份运行）");
                }
                catch (Exception ex)
                {
                    log.Add($"✖ 移除 {keyPath} 失败: {ex.Message}");
                }
            }

            return log;
        }

        public static bool IsApplied() => IsApplied(Registry.LocalMachine);

        /// <summary>是否已存在我们写入的策略</summary>
        internal static bool IsApplied(RegistryKey hive)
        {
            try
            {
                foreach (var (keyPath, valueName, value) in Policies)
                {
                    using var key = hive.OpenSubKey(keyPath);
                    if (key == null) continue;
                    if (string.Equals(key.GetValue(valueName) as string, value, StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>把策略说明压成一句人话，用于日志</summary>
        private static string ShortName(string keyPath)
        {
            if (keyPath.Contains("Chrome")) return "Chrome";
            if (keyPath.Contains("Edge")) return "Edge";
            if (keyPath.Contains("Brave")) return "Brave";
            if (keyPath.Contains("Firefox")) return "Firefox";
            return keyPath;
        }
    }
}
