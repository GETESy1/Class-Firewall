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
                    "bilibili.com", "www.bilibili.com", "m.bilibili.com",
                    "api.bilibili.com", "b23.tv",
                    "hdslb.com", "i0.hdslb.com", "i1.hdslb.com", "i2.hdslb.com",
                    "upos-sz-mirrorcos.bilivideo.com", "bilivideo.com"
                }
            },
            new SiteInfo
            {
                Name = "腾讯视频",
                Domains = new[]
                {
                    "v.qq.com", "film.qq.com", "m.v.qq.com",
                    "vv.video.qq.com", "puui.qpic.cn", "qpic.cn"
                }
            },
            new SiteInfo
            {
                Name = "爱奇艺",
                Domains = new[]
                {
                    "iqiyi.com", "www.iqiyi.com", "m.iqiyi.com",
                    "api.iqiyi.com", "cache.video.iqiyi.com",
                    "qy.net", "iqiyipic.com"
                }
            },
            new SiteInfo
            {
                Name = "优酷",
                Domains = new[]
                {
                    "youku.com", "www.youku.com", "m.youku.com",
                    "api.youku.com", "ups.youku.com",
                    "ykimg.com", "valf.atm.youku.com"
                }
            },
            new SiteInfo
            {
                Name = "芒果TV",
                Domains = new[]
                {
                    "mgtv.com", "www.mgtv.com", "m.mgtv.com",
                    "pcweb.mgtv.com", "d.mgtv.com", "img.mgtv.com"
                }
            },

            // ================= 音乐平台 =================
            new SiteInfo
            {
                Name = "网易云音乐",
                Domains = new[]
                {
                    "music.163.com", "m.music.163.com",
                    "interface.music.163.com", "interface3.music.163.com",
                    "music.163.com", "p1.music.126.net", "p2.music.126.net",
                    "p3.music.126.net", "p4.music.126.net",
                    "126.net", "music.126.net"
                }
            },
            new SiteInfo
            {
                Name = "QQ音乐",
                Domains = new[]
                {
                    "y.qq.com", "i.y.qq.com", "c.y.qq.com", "m.y.qq.com",
                    "u.y.qq.com", "isure.stream.qqmusic.qq.com",
                    "aqqmusic.tc.qq.com", "qqmusic.qq.com",
                    "streamoc.music.tc.qq.com"
                }
            },
            new SiteInfo
            {
                Name = "酷狗音乐",
                Domains = new[]
                {
                    "kugou.com", "www.kugou.com", "m.kugou.com",
                    "login.user.kugou.com", "mobilecdn.kugou.com",
                    "trackercdn.kugou.com", "fanxing.kugou.com",
                    "kglink.kugou.com", "kugou.net"
                }
            },
            new SiteInfo
            {
                Name = "酷我音乐",
                Domains = new[]
                {
                    "kuwo.cn", "www.kuwo.cn", "m.kuwo.cn",
                    "kuwo.com", "nmobi.kuwo.cn", "mobile.kuwo.cn",
                    "sr.sycdn.kuwo.cn", "sycdn.kuwo.cn"
                }
            },

            // ================= 短视频 =================
            new SiteInfo
            {
                Name = "抖音 (Douyin)",
                Domains = new[]
                {
                    "douyin.com", "www.douyin.com", "m.douyin.com", "v.douyin.com",
                    "aweme.snssdk.com", "api.amemv.com", "api-hl.amemv.com",
                    "api3-normal-c-lf.amemv.com", "api5-normal-c-lf.amemv.com",
                    "snssdk.com", "amemv.com",
                    "lf-cdn-tos.bytescm.com", "lf3-cdn-tos.bytescm.com",
                    "douyinpic.com", "p3-sign.douyinpic.com", "p9-sign.douyinpic.com",
                    "douyinvod.com", "v26-web.douyinvod.com", "v3-dy-o.zjcdn.com"
                }
            },
            new SiteInfo
            {
                Name = "快手 (Kuaishou)",
                Domains = new[]
                {
                    "kuaishou.com", "www.kuaishou.com", "m.kuaishou.com", "v.kuaishou.com",
                    "live.kuaishou.com", "api.kuaishouzt.com",
                    "kwaicdn.com", "kwimgs.com", "js2.a.kwimgs.com",
                    "txmov2.a.kwimgs.com", "kuaishoupay.com"
                }
            },
            new SiteInfo
            {
                Name = "TikTok",
                Domains = new[]
                {
                    "tiktok.com", "www.tiktok.com", "m.tiktok.com",
                    "tiktokv.com", "tiktokcdn.com", "musical.ly"
                }
            },

            // ================= 社交社区 =================
            new SiteInfo
            {
                Name = "小红书",
                Domains = new[]
                {
                    "xiaohongshu.com", "www.xiaohongshu.com", "xhslink.com",
                    "xhscdn.com", "sns-video-hw.xhscdn.com"
                }
            },
            new SiteInfo
            {
                Name = "微博",
                Domains = new[]
                {
                    "weibo.com", "www.weibo.com", "m.weibo.cn", "weibo.cn",
                    "api.weibo.cn", "sinaimg.cn", "sina.com.cn"
                }
            },
            new SiteInfo
            {
                Name = "知乎",
                Domains = new[]
                {
                    "zhihu.com", "www.zhihu.com", "m.zhihu.com",
                    "api.zhihu.com", "zhimg.com", "pic1.zhimg.com"
                }
            },
            new SiteInfo
            {
                Name = "豆瓣",
                Domains = new[]
                {
                    "douban.com", "www.douban.com", "m.douban.com",
                    "api.douban.com", "doubanio.com", "img1.doubanio.com"
                }
            },

            // ================= 直播平台 =================
            new SiteInfo
            {
                Name = "斗鱼直播",
                Domains = new[]
                {
                    "douyu.com", "www.douyu.com", "m.douyu.com",
                    "douyucdn.cn", "douyucdn2.cn", "douyutv.com"
                }
            },
            new SiteInfo
            {
                Name = "虎牙直播",
                Domains = new[]
                {
                    "huya.com", "www.huya.com", "m.huya.com",
                    "huyacdn.com", "msstatic.com"
                }
            },

            // ================= 游戏平台 =================
            new SiteInfo
            {
                Name = "WeGame",
                Domains = new[]
                {
                    "wegame.com.cn", "www.wegame.com.cn", "wegame.com"
                }
            },
            new SiteInfo
            {
                Name = "4399 小游戏",
                Domains = new[]
                {
                    "4399.com", "www.4399.com", "s.4399.com",
                    "4399pk.com", "img.4399.net"
                }
            }
        };
    }
}