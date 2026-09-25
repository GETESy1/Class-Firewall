using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClassFirewall
{
    public sealed class AppSettings
    {
        public List<string> CheckedSites { get; set; } = new();
        public bool DpiEnabled { get; set; }
        public bool DnsEnabled { get; set; }
        public bool AutoStart { get; set; }
        public bool AutoBlock { get; set; }
    }

    public static class SettingsStore
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassFirewall");

        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

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