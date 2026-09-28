
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ClassFirewall
{

    internal sealed class DomainMatcher
    {
        private readonly ConcurrentDictionary<string, byte> _set =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count => _set.Count;

        public void Clear() => _set.Clear();

        public void Update(IEnumerable<string> domains)
        {
            _set.Clear();
            if (domains == null) return;

            foreach (var raw in domains)
            {
                if (raw == null) continue;

                var d = raw.Trim().ToLowerInvariant();
                while (d.StartsWith(".")) d = d.Substring(1);   

                if (d.Length > 0) _set[d] = 1;
            }
        }

        public bool IsBlocked(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return false;

            domain = domain.Trim().TrimEnd('.').ToLowerInvariant();
            if (domain.Length == 0) return false;

            if (_set.ContainsKey(domain)) return true;

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
