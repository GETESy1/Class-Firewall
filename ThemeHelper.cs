using System;
using System.Collections.Generic;
using Microsoft.Win32;
using Microsoft.UI.Xaml;

namespace ClassFirewall
{
    /// <summary>主题：跟随系统 / 浅色 / 深色</summary>
    public enum AppTheme
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    internal static class ThemeHelper
    {
        public static IReadOnlyList<ThemeOption> Options { get; } = new[]
        {
            new ThemeOption(AppTheme.System, "跟随系统"),
            new ThemeOption(AppTheme.Light,  "浅色"),
            new ThemeOption(AppTheme.Dark,   "深色")
        };

        public static string DisplayName(AppTheme theme) => theme switch
        {
            AppTheme.Light => "浅色",
            AppTheme.Dark => "深色",
            _ => "跟随系统"
        };

        /// <summary>把设置里的主题落到窗口根元素上</summary>
        public static void Apply(FrameworkElement root, AppTheme theme)
        {
            if (root == null) return;
            root.RequestedTheme = Resolve(theme);
        }

        public static ElementTheme Resolve(AppTheme theme) => theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => SystemUsesLightTheme() ? ElementTheme.Light : ElementTheme.Dark
        };

        /// <summary>读注册表判断系统当前是浅色还是深色（AppsUseLightTheme = 1 为浅色）</summary>
        public static bool SystemUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
            }
            catch { }
            return true;
        }

        public sealed record ThemeOption(AppTheme Value, string Label)
        {
            public override string ToString() => Label;
        }
    }
}
