using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ClassFirewall
{
    public static class DnsConfigurator
    {
        public static List<string> GetActiveInterfaces()
        {
            var list = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                list.Add(ni.Name);
            }
            return list;
        }

        public static void SetDnsToLocalhost()
        {
            foreach (var name in GetActiveInterfaces())
            {
                RunNetsh($"interface ipv4 set dnsservers name=\"{name}\" static 127.0.0.1 primary");
            }
        }

        public static void RestoreDhcpDns()
        {
            foreach (var name in GetActiveInterfaces())
            {
                RunNetsh($"interface ipv4 set dnsservers name=\"{name}\" source=dhcp");
            }
        }

        private static void RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { /* 忽略单个网卡失败 */ }
        }
    }
}