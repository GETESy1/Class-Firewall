// ==================== DomainMatcher.cs ====================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ClassFirewall
{
    /// <summary>
    /// 域名黑名单匹配器（DNS / DPI / TUN 三层共用同一个实现）。
    ///
    /// 为什么单独抽出来：这段"逐级后缀匹配"的逻辑原本在三个类里各写了一份，
    /// 其中两份把 <c>Substring(idx)</c> 写成了带前导点的形式（".douyin.com"），
    /// 永远匹配不到黑名单里的 "douyin.com" —— 导致子域名实际上没被拦住。
    /// 现在只有一份实现，并配了单元测试。
    ///
    /// 匹配规则：命中自身，或命中任意一级后缀。例如黑名单含 "douyin.com" 时，
    /// "douyin.com"、"www.douyin.com"、"api-hl.amemv.douyin.com" 都命中。
    /// </summary>
    internal sealed class DomainMatcher
    {
        private readonly ConcurrentDictionary<string, byte> _set =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count => _set.Count;

        public void Clear() => _set.Clear();

        /// <summary>用新的域名集合替换现有黑名单</summary>
        public void Update(IEnumerable<string> domains)
        {
            _set.Clear();
            if (domains == null) return;

            foreach (var raw in domains)
            {
                if (raw == null) continue;

                var d = raw.Trim().ToLowerInvariant();
                while (d.StartsWith(".")) d = d.Substring(1);   // 容忍 ".example.com" 写法

                if (d.Length > 0) _set[d] = 1;
            }
        }

        /// <summary>该域名是否命中黑名单（含各级子域名）</summary>
        public bool IsBlocked(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return false;

            domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
            if (domain.Length == 0) return false;

            if (_set.ContainsKey(domain)) return true;

            // 逐级剥掉最左边的标签来比对后缀。
            // ★ 注意 idx + 1：必须跳过那个点，否则得到的是 ".douyin.com"，永远匹配不上。
            int idx = domain.IndexOf('.');
            while (idx > 0)
            {
                var suffix = domain.Substring(idx + 1);
                if (suffix.Length == 0) break;
                if (_set.ContainsKey(suffix)) return true;
                idx = domain.IndexOf('.', idx + 1);
            }

            return false;
        }
    }
}
