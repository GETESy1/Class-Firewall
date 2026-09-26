using System;
using System.Collections.Generic;

namespace ClassFirewall
{
    public sealed class SiteInfo
    {
        public string Name { get; init; } = "";
        public string[] Domains { get; init; } = Array.Empty<string>();

        public override string ToString() => Name;
    }

    /// <summary>
    /// 站点黑名单清单。
    ///
    /// ★ 匹配规则（见 DomainMatcher）：**逐级后缀匹配**。
    ///   清单里写 "douyin.com"，则 douyin.com 及其**任意深度子域名**全部命中：
    ///   www.douyin.com、api-hl.amemv.douyin.com、以及随便什么.douyin.com 都会被拦。
    ///
    /// ★ 所以新增站点时：**优先只写基础域名**（两段式，如 kuwo.cn、4399.net），
    ///   不必再逐个列举 www / m / api 这些子域名 —— 它们已经被基础域名覆盖了。
    ///
    /// ★ 唯一的例外：基础域名被**不相关的服务共用**时不能整片封，
    ///   这种只能逐条列举具体子域名。目前有两处：
    ///     · 腾讯的 qq.com —— QQ、微信网页版、QQ邮箱、腾讯网都在上面
    ///     · 网易的 163.com —— 163邮箱、网易新闻
    /// </summary>
    public static class SiteCatalog
    {
        public static IReadOnlyList<SiteInfo> Sites { get; } = new List<SiteInfo>
        {
            // ================= 视频平台 =================
            new SiteInfo
            {
                Name = "哔哩哔哩 (Bilibili)",
                Domains = new[]
                {
                    "bilibili.com",     // 主站（www / m / api 等全部由它覆盖）
                    "hdslb.com",        // 图片 CDN（含 i0 / i1 / i2）
                    "bilivideo.com",    // 视频 CDN
                    "b23.tv"            // 短链
                }
            },
            new SiteInfo
            {
                Name = "腾讯视频",
                // 不能封 qq.com（会连带封掉 QQ / 微信网页版 / QQ邮箱），只能列具体子域
                Domains = new[]
                {
                    "v.qq.com",         // 主站（含 m.v.qq.com）
                    "film.qq.com",
                    "video.qq.com",     // 视频接口（含 vv.video.qq.com）
                    "qpic.cn"           // 图片 CDN（含 puui.qpic.cn）
                }
            },
            new SiteInfo
            {
                Name = "爱奇艺",
                Domains = new[]
                {
                    "iqiyi.com",        // 主站（含 www / m / api / cache.video）
                    "qy.net",
                    "iqiyipic.com"
                }
            },
            new SiteInfo
            {
                Name = "优酷",
                Domains = new[]
                {
                    "youku.com",        // 主站（含 www / m / api / ups / valf.atm）
                    "ykimg.com"
                }
            },
            new SiteInfo
            {
                Name = "芒果TV",
                Domains = new[]
                {
                    "mgtv.com"          // 全部子域（www / m / pcweb / d / img）
                }
            },

            // ================= 音乐平台 =================
            new SiteInfo
            {
                Name = "网易云音乐",
                // 不能封 163.com（163邮箱 / 网易新闻），主站只列 music.163.com
                Domains = new[]
                {
                    "music.163.com",    // 含 m / interface / interface3
                    "126.net"           // CDN 基础域名，任意子域全拦
                }
            },
            new SiteInfo
            {
                Name = "QQ音乐",
                // 同样不能封 qq.com
                Domains = new[]
                {
                    "y.qq.com",         // 含 i / c / m / u
                    "qqmusic.qq.com",   // 含 isure.stream
                    "aqqmusic.tc.qq.com",
                    "streamoc.music.tc.qq.com"
                }
            },
            new SiteInfo
            {
                Name = "酷狗音乐",
                Domains = new[]
                {
                    "kugou.com",        // 含 www / m / login.user / mobilecdn / trackercdn / fanxing / kglink
                    "kugou.net"
                }
            },
            new SiteInfo
            {
                Name = "酷我音乐",
                Domains = new[]
                {
                    "kuwo.cn",          // 含 www / m / nmobi / mobile / sr.sycdn
                    "kuwo.com"
                }
            },

            // ================= 短视频 =================
            new SiteInfo
            {
                Name = "抖音 (Douyin)",
                Domains = new[]
                {
                    "douyin.com",       // 含 www / m / v
                    "snssdk.com",       // 含 aweme
                    "amemv.com",        // 含 api / api-hl / api3-normal-c-lf 等
                    "bytescm.com",      // 含 lf-cdn-tos / lf3-cdn-tos
                    "douyinpic.com",    // 含 p3-sign / p9-sign
                    "douyinvod.com",    // 含 v26-web
                    "v3-dy-o.zjcdn.com" // zjcdn.com 属共用 CDN，只能单列
                }
            },
            new SiteInfo
            {
                Name = "快手 (Kuaishou)",
                Domains = new[]
                {
                    "kuaishou.com",     // 含 www / m / v / live
                    "kuaishouzt.com",   // 含 api
                    "kwaicdn.com",
                    "kwimgs.com",       // 含 js2.a / txmov2.a
                    "kuaishoupay.com"
                }
            },

            // ================= 社交社区 =================
            new SiteInfo
            {
                Name = "小红书",
                Domains = new[]
                {
                    "xiaohongshu.com",  // 含 www
                    "xhslink.com",
                    "xhscdn.com"        // 含 sns-video-hw
                }
            },
            new SiteInfo
            {
                Name = "微博",
                Domains = new[]
                {
                    "weibo.com",        // 含 www
                    "weibo.cn",         // 含 m / api
                    "sinaimg.cn",
                    "sina.com.cn"       // 新浪站群（沿用原有范围）
                }
            },
            new SiteInfo
            {
                Name = "知乎",
                Domains = new[]
                {
                    "zhihu.com",        // 含 www / m / api
                    "zhimg.com"         // 含 pic1 等图片子域
                }
            },
            new SiteInfo
            {
                Name = "豆瓣",
                Domains = new[]
                {
                    "douban.com",       // 含 www / m / api
                    "doubanio.com"      // 含 img1 等图片子域
                }
            },

            // ================= 直播平台 =================
            new SiteInfo
            {
                Name = "斗鱼直播",
                Domains = new[]
                {
                    "douyu.com",        // 含 www / m
                    "douyucdn.cn",
                    "douyucdn2.cn",
                    "douyutv.com"
                }
            },
            new SiteInfo
            {
                Name = "虎牙直播",
                Domains = new[]
                {
                    "huya.com",         // 含 www / m
                    "huyacdn.com",
                    "msstatic.com"
                }
            },

            // ================= 游戏平台 =================
            new SiteInfo
            {
                Name = "4399 小游戏",
                Domains = new[]
                {
                    "4399.com",         // 含 www / s
                    "4399pk.com",
                    "4399.net"          // 含 img 等任意子域
                }
            }
        };
    }
}
