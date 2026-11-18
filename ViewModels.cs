using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ClassFirewall
{
    /// <summary>
    /// 站点列表项（给 ListView 绑定用）。
    ///
    /// WinUI 的 CheckBox 绑定不能直接指到 SiteInfo 上（那是纯数据模型，
    /// 没有变更通知），所以这里包一层可观察对象，勾选状态写回它自己。
    /// </summary>
    public sealed class SiteItem : INotifyPropertyChanged
    {
        private bool _isChecked;

        public SiteItem(SiteInfo site)
        {
            Site = site;
            Name = site.Name;
            Domains = site.Domains;
            DomainHint = string.Join("  ·  ", site.Domains);
        }

        public SiteInfo Site { get; }

        public string Name { get; }

        public string[] Domains { get; }

        /// <summary>列表里那行小字：把域名用 · 连起来</summary>
        public string DomainHint { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CheckedChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>主窗口用的视图数据：站点集合与派生统计</summary>
    internal sealed class MainViewModel
    {
        public List<SiteItem> Sites { get; } = SiteCatalog.Sites
            .Select(s => new SiteItem(s))
            .ToList();

        public IEnumerable<SiteItem> CheckedSites => Sites.Where(s => s.IsChecked);

        public int CheckedCount => Sites.Count(s => s.IsChecked);

        /// <summary>要下发的域名（去重、排序后用于预览）</summary>
        public List<string> SelectedDomains() => CheckedSites
            .SelectMany(s => s.Domains)
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => d.Length > 0 && !d.Contains(' '))
            .Distinct()
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
    }
}
