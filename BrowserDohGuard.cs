using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ClassFirewall
{

    internal static class BrowserDohGuard
    {
        internal static readonly (string KeyPath, string ValueName, string Value)[] Policies =
        {
            (@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off"),
            (@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off"),
            (@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", "off"),
            (@"SOFTWARE\Policies\Mozilla\Firefox", "DNSOverHTTPS", "{\"Enabled\": false}"),
        };
        public static readonly string[] DohDomains =
        {
            "cloudflare-dns.com",       
            "one.one.one.one",          
            "dns.google",               
            "doh.opendns.com",          
            "dns.quad9.net",            
            "dns.nextdns.io",           
            "dns.adguard.com",          
            "doh.cleanbrowsing.org",    
            "dns.mullvad.net",          
            "doh.pub",                  
            "dns.alidns.com",           
            "doh.360.cn",               
        };

        public static List<string> Apply() => Apply(Registry.LocalMachine);

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
                        log.Add($"打不开策略键 {keyPath}");
                        continue;
                    }

                    key.SetValue(valueName, value, RegistryValueKind.String);
                    log.Add($"{ShortName(keyPath)} ← {valueName}={value}");
                }
                catch (UnauthorizedAccessException)
                {
                    log.Add($"无权限写入 {keyPath}（需要以管理员身份运行）");
                }
                catch (Exception ex)
                {
                    log.Add($"写入 {keyPath} 失败: {ex.Message}");
                }
            }

            return log;
        }

        public static List<string> Remove() => Remove(Registry.LocalMachine);

        internal static List<string> Remove(RegistryKey hive)
        {
            var log = new List<string>();

            foreach (var (keyPath, valueName, value) in Policies)
            {
                try
                {
                    using var key = hive.OpenSubKey(keyPath, true);
                    if (key == null) continue;   

                    var current = key.GetValue(valueName) as string;
                    if (current == null) continue;

                    if (!string.Equals(current, value, StringComparison.Ordinal))
                    {
                        log.Add($" {ShortName(keyPath)} 的 {valueName} 不是本程序写的不删除（当前值 {current}）");
                        continue;
                    }

                    key.DeleteValue(valueName, false);
                    log.Add($"已移除 {ShortName(keyPath)} 的 {valueName}");
                }
                catch (UnauthorizedAccessException)
                {
                    log.Add($"无权限移除 {keyPath}（需要以管理员身份运行）");
                }
                catch (Exception ex)
                {
                    log.Add($"移除 {keyPath} 失败: {ex.Message}");
                }
            }

            return log;
        }

        public static bool IsApplied() => IsApplied(Registry.LocalMachine);

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
