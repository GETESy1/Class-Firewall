using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ClassFirewall
{
    /// <summary>用外部 DNS 服务器解析域名（绕过本机的 DNS 服务器）</summary>
    public static class ExternalDns
    {
        private static readonly IPEndPoint[] Upstreams =
        {
            new IPEndPoint(IPAddress.Parse("223.5.5.5"), 53),
            new IPEndPoint(IPAddress.Parse("114.114.114.114"), 53)
        };

        public static async Task<List<string>> ResolveAsync(string domain, CancellationToken token = default)
        {
            var result = new List<string>();
            var query = BuildQuery(domain);

            foreach (var upstream in Upstreams)
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 3000;
                    udp.Connect(upstream);
                    await udp.SendAsync(query, query.Length);

                    var task = udp.ReceiveAsync();
                    var done = await Task.WhenAny(task, Task.Delay(3000, token));
                    if (done != task) continue;

                    var ips = ParseResponse(task.Result.Buffer);
                    if (ips.Count > 0) return ips;
                }
                catch { }
            }
            return result;
        }

        private static byte[] BuildQuery(string domain)
        {
            var buf = new byte[512];
            int p = 0;
            // 头
            buf[p++] = 0x12; buf[p++] = 0x34; // ID
            buf[p++] = 0x01; buf[p++] = 0x00; // flags: standard query
            buf[p++] = 0x00; buf[p++] = 0x01; // QDCOUNT
            buf[p++] = 0x00; buf[p++] = 0x00; // ANCOUNT
            buf[p++] = 0x00; buf[p++] = 0x00;
            buf[p++] = 0x00; buf[p++] = 0x00;

            foreach (var label in domain.Split('.'))
            {
                if (label.Length > 63) return Array.Empty<byte>();
                buf[p++] = (byte)label.Length;
                foreach (var c in label) buf[p++] = (byte)c;
            }
            buf[p++] = 0x00;
            buf[p++] = 0x00; buf[p++] = 0x01; // TYPE A
            buf[p++] = 0x00; buf[p++] = 0x01; // CLASS IN

            var outBuf = new byte[p];
            Array.Copy(buf, outBuf, p);
            return outBuf;
        }

        private static List<string> ParseResponse(byte[] buf)
        {
            var ips = new List<string>();
            if (buf.Length < 12) return ips;

            int qd = (buf[4] << 8) | buf[5];
            int an = (buf[6] << 8) | buf[7];
            if (an == 0) return ips;

            int pos = 12;
            // 跳过 Questions
            for (int i = 0; i < qd && pos < buf.Length; i++)
            {
                while (pos < buf.Length && buf[pos] != 0)
                {
                    if ((buf[pos] & 0xC0) == 0xC0) { pos += 2; break; }
                    pos += 1 + buf[pos];
                }
                if (pos < buf.Length && buf[pos] == 0) pos++;
                pos += 4; // TYPE + CLASS
            }

            // 解析 Answers
            for (int i = 0; i < an && pos + 10 < buf.Length; i++)
            {
                if ((buf[pos] & 0xC0) == 0xC0) pos += 2;
                else { while (pos < buf.Length && buf[pos] != 0) pos += 1 + buf[pos]; pos++; }

                if (pos + 10 > buf.Length) break;
                int type = (buf[pos] << 8) | buf[pos + 1];
                pos += 8; // TYPE(2) + CLASS(2) + TTL(4)
                int rdlen = (buf[pos] << 8) | buf[pos + 1];
                pos += 2;

                if (pos + rdlen > buf.Length) break;

                if (type == 1 && rdlen == 4)
                {
                    ips.Add($"{buf[pos]}.{buf[pos + 1]}.{buf[pos + 2]}.{buf[pos + 3]}");
                }
                pos += rdlen;
            }
            return ips;
        }
    }
}