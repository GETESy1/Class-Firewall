// ==================== LegacyHostsCleaner.cs ====================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// 旧版本遗留的 hosts 标记块清理器。
    ///
    /// ★ 本程序不再使用 hosts 文件做屏蔽（易被应用绕过、且清理麻烦），
    ///   屏蔽完全依赖 DNS + DPI + TUN 三层。
    ///   这里只负责在启动/解除时把旧版本写入的标记块从 hosts 中摘掉，
    ///   保证从旧版本升级上来的机器不会残留任何 hosts 记录。
    ///
    /// 只删除本程序自己写入的标记块，绝不触碰用户自己的 hosts 内容。
    /// </summary>
    public static class LegacyHostsCleaner
    {
        public static string HostsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");

        // 与旧版本 HostsManager 写入的标记保持一致；用前缀匹配以兼容历史变体
        private const string StartPrefix = "# ==== Class Firewall Start";
        private const string EndPrefix = "# ==== Class Firewall End";

        public const int ResultNothingToDo = 0;
        public const int ResultRefusedUnbalanced = -1;

        /// <summary>hosts 中是否还存在旧版本的标记块</summary>
        public static bool HasLegacyBlock()
        {
            try
            {
                if (!File.Exists(HostsPath)) return false;

                foreach (var raw in File.ReadAllLines(HostsPath))
                {
                    if (raw.Trim().StartsWith(StartPrefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { /* 读不到就当没有 */ }
            return false;
        }

        /// <summary>
        /// 移除旧版本遗留的 hosts 标记块。
        /// 返回被删除的行数；
        /// <see cref="ResultNothingToDo"/> 表示没有遗留内容，
        /// <see cref="ResultRefusedUnbalanced"/> 表示标记不成对（为安全起见已放弃修改）。
        /// </summary>
        public static int Purge()
        {
            try
            {
                if (!File.Exists(HostsPath)) return ResultNothingToDo;

                var lines = File.ReadAllLines(HostsPath);
                var kept = FilterLines(lines, out int removed);

                if (kept == null) return ResultRefusedUnbalanced;   // 标记不成对，放弃
                if (removed == 0) return ResultNothingToDo;         // 没有遗留块，不重写文件

                var fi = new FileInfo(HostsPath);
                if (fi.IsReadOnly) fi.IsReadOnly = false;

                // 去掉文件末尾多余空行，避免每次清理都增长
                while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
                    kept.RemoveAt(kept.Count - 1);

                var sb = new StringBuilder();
                foreach (var l in kept) sb.AppendLine(l);

                File.WriteAllText(HostsPath, sb.ToString(), new UTF8Encoding(false));

                DnsConfigurator.FlushCache();   // 清理后刷新一下缓存
                return removed;
            }
            catch
            {
                return ResultNothingToDo;   // 无权限或文件被占用：不影响主流程，静默失败
            }
        }

        /// <summary>
        /// 纯函数：从 hosts 行中剔除旧版本标记块。
        /// 返回保留的行；若标记不成对则返回 null（调用方应放弃修改）。
        /// <paramref name="removed"/> 为被移除的行数。
        /// </summary>
        internal static List<string>? FilterLines(string[] lines, out int removed)
        {
            removed = 0;

            // ★ 安全检查：Start / End 必须成对，否则宁可不动，
            //   避免 End 标记丢失时把用户自己的 hosts 内容一并删掉。
            int starts = 0, ends = 0;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith(StartPrefix, StringComparison.OrdinalIgnoreCase)) starts++;
                else if (line.StartsWith(EndPrefix, StringComparison.OrdinalIgnoreCase)) ends++;
            }

            if (starts == 0) return new List<string>(lines);     // 没有遗留块
            if (starts != ends) return null;                     // 标记不成对，放弃

            var kept = new List<string>();
            bool inBlock = false;

            foreach (var raw in lines)
            {
                var line = raw.Trim();

                if (!inBlock && line.StartsWith(StartPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = true;
                    removed++;           // 标记行本身也算被移除
                    continue;
                }
                if (inBlock && line.StartsWith(EndPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = false;
                    removed++;
                    continue;
                }
                if (inBlock)
                {
                    removed++;
                    continue;            // 丢弃旧版本写入的映射行
                }

                kept.Add(raw);
            }

            return kept;
        }

        /// <summary>旧版本的备份文件是否还在（供日志提示用）</summary>
        public static bool HasLegacyBackup()
        {
            try { return File.Exists(HostsPath + ".classfirewall.bak"); }
            catch { return false; }
        }
    }
}
