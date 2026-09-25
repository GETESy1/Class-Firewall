// ==================== HostsManager.cs ====================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// 读写系统 hosts 文件。用标记块管理自己的条目，不破坏用户原有内容。
    /// </summary>
    public static class HostsManager
    {
        public static string HostsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");

        public static string BackupPath => HostsPath + ".classfirewall.bak";

        private const string StartMark = "# ==== Class Firewall Start ==== (请勿手动修改)";
        private const string EndMark = "# ==== Class Firewall End ====";
        private const string RedirectIp = "127.0.0.1";

        /// <summary>读取当前 hosts 中由本程序写入的域名</summary>
        public static List<string> GetBlockedDomains()
        {
            var list = new List<string>();
            if (!File.Exists(HostsPath)) return list;

            bool inBlock = false;
            foreach (var raw in File.ReadAllLines(HostsPath))
            {
                var line = raw.Trim();
                if (line.Equals(StartMark, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
                if (line.Equals(EndMark, StringComparison.OrdinalIgnoreCase)) { inBlock = false; continue; }
                if (!inBlock || line.Length == 0 || line.StartsWith("#")) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) list.Add(parts[1].ToLowerInvariant());
            }
            return list;
        }

        /// <summary>把域名列表写入 hosts（覆盖上一次的规则）</summary>
        public static void Apply(IEnumerable<string> domains)
        {
            var clean = domains
                .Select(d => d.Trim().ToLowerInvariant())
                .Where(d => d.Length > 0 && !d.Contains(' '))
                .Distinct()
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();

            BackupOnce();

            var lines = ReadHostsWithoutOurBlock();
            var sb = new StringBuilder();
            foreach (var l in lines) sb.AppendLine(l);

            if (clean.Count > 0)
            {
                sb.AppendLine(StartMark);
                foreach (var d in clean) sb.AppendLine($"{RedirectIp} {d}");
                sb.AppendLine(EndMark);
            }

            WriteHosts(sb.ToString());
            FlushDns();
        }

        /// <summary>移除本程序写入的全部规则</summary>
        public static void Clear() => Apply(Array.Empty<string>());

        private static void BackupOnce()
        {
            try
            {
                if (File.Exists(HostsPath) && !File.Exists(BackupPath))
                    File.Copy(HostsPath, BackupPath, false);
            }
            catch { /* 备份失败不影响主流程 */ }
        }

        private static List<string> ReadHostsWithoutOurBlock()
        {
            var result = new List<string>();
            if (!File.Exists(HostsPath)) return result;

            bool inBlock = false;
            foreach (var raw in File.ReadAllLines(HostsPath))
            {
                var line = raw.Trim();
                if (line.Equals(StartMark, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
                if (line.Equals(EndMark, StringComparison.OrdinalIgnoreCase)) { inBlock = false; continue; }
                if (inBlock) continue;
                result.Add(raw);
            }

            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
                result.RemoveAt(result.Count - 1);

            return result;
        }

        private static void WriteHosts(string content)
        {
            var fi = new FileInfo(HostsPath);
            if (fi.IsReadOnly) fi.IsReadOnly = false;
            File.WriteAllText(HostsPath, content, new UTF8Encoding(false));
        }

        /// <summary>清空系统 DNS 缓存</summary>
        public static void FlushDns()
        {
            try
            {
                var psi = new ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { /* 忽略 */ }
        }
    }
}