using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassFirewall
{
    public sealed class AppSettings
    {
        /// <summary>勾选的站点名（对应 SiteCatalog 里的 Name）</summary>
        public List<string> CheckedSites { get; set; } = new();

        /// <summary>是否启用屏蔽（启动本地 DNS 并接管系统 DNS）</summary>
        public bool DnsEnabled { get; set; }

        public bool AutoStart { get; set; }

        /// <summary>启动时自动恢复上次的屏蔽配置</summary>
        public bool AutoBlock { get; set; }

        // ---- 以下字段已废弃，仅为了能读旧版本的 settings.json 而保留 ----
        // 它们对应的功能（DPI 深度包检查 / TUN 模式）已下线，
        // 读到 true 时会在日志里提示一次，不会影响运行。

        [JsonInclude]
        public bool DpiEnabled { get; set; }

        [JsonInclude]
        public bool TunEnabled { get; set; }
    }

    public static class SettingsStore
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassFirewall");

        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static string SettingsPath => FilePath;

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(s,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
