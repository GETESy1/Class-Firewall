// ==================== PacketCodec.cs ====================
using System;
using System.Buffers.Binary;
using System.Text;

namespace ClassFirewall
{
    /// <summary>
    /// IPv4 / TCP / UDP / DNS 报文的编解码。
    ///
    /// 全部是纯函数（只依赖传入的字节），不碰任何系统资源，
    /// 因此可以直接单元测试 —— TUN 引擎里最容易写错的就是校验和与偏移量。
    /// </summary>
    internal static class PacketCodec
    {
        public const byte ProtocolIcmp = 1;
        public const byte ProtocolTcp = 6;
        public const byte ProtocolUdp = 17;

        public const int Ipv4MinHeader = 20;
        public const int TcpMinHeader = 20;
        public const int UdpHeader = 8;

        // ---------------- IPv4 ----------------

        internal sealed class Ipv4Packet
        {
            public int HeaderLength;
            public int TotalLength;
            public byte Protocol;
            public uint SourceAddress;
            public uint DestinationAddress;
            public int PayloadOffset;
            public int PayloadLength;
            public byte[] Raw = Array.Empty<byte>();
        }

        /// <summary>解析 IPv4 头。长度/版本不合法时返回 false</summary>
        public static bool TryParseIpv4(byte[] buf, out Ipv4Packet packet)
        {
            packet = new Ipv4Packet();
            if (buf == null || buf.Length < Ipv4MinHeader) return false;
            if ((buf[0] >> 4) != 4) return false;                 // version 必须为 4

            int ihl = (buf[0] & 0x0F) * 4;
            if (ihl < Ipv4MinHeader || ihl > buf.Length) return false;

            int total = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2, 2));
            if (total < ihl) return false;

            // 报文可能被截断（TUN 上一般不会），按实际可用长度裁剪
            int usable = Math.Min(total, buf.Length);

            packet.HeaderLength = ihl;
            packet.TotalLength = total;
            packet.Protocol = buf[9];
            packet.SourceAddress = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12, 4));
            packet.DestinationAddress = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16, 4));
            packet.PayloadOffset = ihl;
            packet.PayloadLength = usable - ihl;
            packet.Raw = buf;
            return true;
        }

        /// <summary>构造一个 IPv4 报文（ID 递增，DF 置位，TTL=64）</summary>
        public static byte[] BuildIpv4(byte protocol, uint source, uint destination, byte[] payload, ushort id = 1)
        {
            int total = Ipv4MinHeader + payload.Length;
            var buf = new byte[total];

            buf[0] = 0x45;                                        // IPv4, IHL=5
            buf[1] = 0x00;                                        // DSCP/ECN
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2, 2), (ushort)total);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(4, 2), id);
            buf[6] = 0x40;                                        // Don't Fragment
            buf[7] = 0x00;
            buf[8] = 64;                                          // TTL
            buf[9] = protocol;
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(12, 4), source);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(16, 4), destination);

            int csum = ComputeChecksum(buf.AsSpan(0, Ipv4MinHeader));
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(10, 2), (ushort)csum);

            Array.Copy(payload, 0, buf, Ipv4MinHeader, payload.Length);
            return buf;
        }

        /// <summary>RFC 1071 16 位反码求和校验和（已取反，可直接写入报文）</summary>
        public static ushort ComputeChecksum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            int i = 0;
            for (; i + 1 < data.Length; i += 2)
                sum += (uint)((data[i] << 8) | data[i + 1]);

            if (i < data.Length)                                  // 奇数长度补 0
                sum += (uint)(data[i] << 8);

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)~sum;
        }

        // ---------------- 地址辅助 ----------------

        public static uint ParseIpv4Address(string ip)
        {
            var parts = ip.Split('.');
            if (parts.Length != 4) throw new FormatException("非法 IPv4: " + ip);

            uint result = 0;
            foreach (var p in parts)
            {
                if (!byte.TryParse(p, out byte b)) throw new FormatException("非法 IPv4: " + ip);
                result = (result << 8) | b;
            }
            return result;
        }

        public static string FormatIpv4Address(uint addr) =>
            $"{(addr >> 24) & 0xFF}.{(addr >> 16) & 0xFF}.{(addr >> 8) & 0xFF}.{addr & 0xFF}";

        // ---------------- UDP ----------------

        internal sealed class UdpDatagram
        {
            public int SourcePort;
            public int DestinationPort;
            public int PayloadOffset;
            public int PayloadLength;
        }

        public static bool TryParseUdp(byte[] buf, Ipv4Packet ip, out UdpDatagram udp)
        {
            udp = new UdpDatagram();
            if (ip.PayloadLength < UdpHeader) return false;

            int off = ip.PayloadOffset;
            int len = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off + 4, 2));
            if (len < UdpHeader) return false;

            udp.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off, 2));
            udp.DestinationPort = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off + 2, 2));
            udp.PayloadOffset = off + UdpHeader;
            udp.PayloadLength = Math.Min(len - UdpHeader, ip.PayloadLength - UdpHeader);
            return true;
        }

        /// <summary>构造 UDP 报文（含伪首部校验和）</summary>
        public static byte[] BuildUdp(uint source, uint destination,
            int sourcePort, int destinationPort, byte[] payload)
        {
            var seg = new byte[UdpHeader + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(0, 2), (ushort)sourcePort);
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(2, 2), (ushort)destinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(4, 2), (ushort)seg.Length);
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(6, 2), 0);   // 校验和占位
            Array.Copy(payload, 0, seg, UdpHeader, payload.Length);

            ushort csum = ComputeTransportChecksum(source, destination, ProtocolUdp, seg);
            // UDP 校验和为 0 表示"未计算"，这里若真算出 0 则写成 0xFFFF
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(6, 2), csum == 0 ? (ushort)0xFFFF : csum);

            return BuildIpv4(ProtocolUdp, source, destination, seg);
        }

        // ---------------- TCP ----------------

        internal sealed class TcpSegment
        {
            public int SourcePort;
            public int DestinationPort;
            public uint SequenceNumber;
            public uint AcknowledgmentNumber;
            public int HeaderLength;
            public byte Flags;
            public int PayloadOffset;
            public int PayloadLength;

            public bool Syn => (Flags & 0x02) != 0;
            public bool Ack => (Flags & 0x10) != 0;
            public bool Fin => (Flags & 0x01) != 0;
            public bool Rst => (Flags & 0x04) != 0;
        }

        public static bool TryParseTcp(byte[] buf, Ipv4Packet ip, out TcpSegment tcp)
        {
            tcp = new TcpSegment();
            if (ip.PayloadLength < TcpMinHeader) return false;

            int off = ip.PayloadOffset;
            int headerLen = (buf[off + 12] >> 4) * 4;
            if (headerLen < TcpMinHeader || headerLen > ip.PayloadLength) return false;

            tcp.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off, 2));
            tcp.DestinationPort = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(off + 2, 2));
            tcp.SequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(off + 4, 4));
            tcp.AcknowledgmentNumber = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(off + 8, 4));
            tcp.HeaderLength = headerLen;
            tcp.Flags = buf[off + 13];
            tcp.PayloadOffset = off + headerLen;
            tcp.PayloadLength = ip.PayloadLength - headerLen;
            return true;
        }

        /// <summary>
        /// 针对收到的 TCP 段构造一条 RST 响应，源/目的互换。
        /// 让客户端立刻得到"连接被重置"，而不是静默超时。
        /// </summary>
        public static byte[] BuildTcpReset(byte[] buf, Ipv4Packet ip, TcpSegment tcp)
        {
            var seg = new byte[TcpMinHeader];
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(0, 2), (ushort)tcp.DestinationPort);
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(2, 2), (ushort)tcp.SourcePort);

            // 对方发了 ACK 就用它的 ack 作为我们的 seq，否则 seq=0（RFC 793）
            uint seq = tcp.Ack ? tcp.AcknowledgmentNumber : 0u;

            // 我们的 ack 要把对方的数据/SYN/FIN 都算进去
            uint ack = tcp.SequenceNumber + (uint)tcp.PayloadLength;
            if (tcp.Syn) ack++;
            if (tcp.Fin) ack++;

            BinaryPrimitives.WriteUInt32BigEndian(seg.AsSpan(4, 4), seq);
            BinaryPrimitives.WriteUInt32BigEndian(seg.AsSpan(8, 4), ack);
            seg[12] = 0x50;                                       // 数据偏移 = 5 (20 字节)
            seg[13] = 0x14;                                       // RST | ACK
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(14, 2), 0);   // 窗口 = 0
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(16, 2), 0);   // 校验和占位
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(18, 2), 0);   // 紧急指针 = 0

            ushort csum = ComputeTransportChecksum(
                ip.DestinationAddress, ip.SourceAddress, ProtocolTcp, seg);
            BinaryPrimitives.WriteUInt16BigEndian(seg.AsSpan(16, 2), csum);

            return BuildIpv4(ProtocolTcp, ip.DestinationAddress, ip.SourceAddress, seg);
        }

        /// <summary>TCP/UDP 校验和：伪首部 + 报文段</summary>
        public static ushort ComputeTransportChecksum(
            uint source, uint destination, byte protocol, ReadOnlySpan<byte> segment)
        {
            uint sum = 0;

            sum += (source >> 16) & 0xFFFF;
            sum += source & 0xFFFF;
            sum += (destination >> 16) & 0xFFFF;
            sum += destination & 0xFFFF;
            sum += protocol;
            sum += (uint)segment.Length;

            int i = 0;
            for (; i + 1 < segment.Length; i += 2)
                sum += (uint)((segment[i] << 8) | segment[i + 1]);
            if (i < segment.Length)
                sum += (uint)(segment[i] << 8);

            while ((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)~sum;
        }

        // ---------------- DNS ----------------

        /// <summary>从 DNS 查询报文中取出问题域名（小写，无尾点）</summary>
        public static bool TryParseDnsQuestion(byte[] buf, int offset, int length,
            out string qname, out int questionEnd)
        {
            qname = "";
            questionEnd = 0;
            if (length < 12) return false;

            var sb = new StringBuilder();
            int pos = offset + 12;
            int end = offset + length;

            while (pos < end)
            {
                int len = buf[pos];
                if (len == 0) { pos++; break; }
                if ((len & 0xC0) == 0xC0) { pos += 2; break; }     // 压缩指针（查询里不该出现）
                pos++;
                if (pos + len > end) return false;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(buf, pos, len));
                pos += len;
            }

            if (pos + 4 > end) return false;                      // QTYPE + QCLASS
            questionEnd = pos + 4;
            qname = sb.ToString().ToLowerInvariant();
            return qname.Length > 0;
        }

        /// <summary>
        /// 构造 DNS 应答：把问题原样带回，并追加一条 A 记录。
        /// 用于把命中黑名单的域名指向本地（127.0.0.1）。
        /// </summary>
        public static byte[] BuildDnsAResponse(byte[] query, int queryOffset, int queryLength,
            int questionEnd, uint answerAddress, uint ttlSeconds = 60)
        {
            int questionLength = questionEnd - (queryOffset + 12);
            if (questionLength < 0 || queryOffset + 12 + questionLength > query.Length)
                throw new ArgumentException("questionEnd 超出查询报文范围", nameof(questionEnd));

            var resp = new byte[12 + questionLength + 16];
            int qEnd = 12 + questionLength;

            // 只拷贝到问题结束为止，避免把请求尾部的额外记录带进来
            Array.Copy(query, queryOffset, resp, 0, 12 + questionLength);

            resp[2] = 0x81;                                       // QR=1, RD=1
            resp[3] = 0x80;                                       // RA=1, rcode=0
            resp[6] = 0x00; resp[7] = 0x01;                       // ANCOUNT = 1
            resp[8] = 0x00; resp[9] = 0x00;                       // NSCOUNT = 0
            resp[10] = 0x00; resp[11] = 0x00;                     // ARCOUNT = 0

            int p = qEnd;
            resp[p++] = 0xC0; resp[p++] = 0x0C;                   // 指向问题中的域名
            resp[p++] = 0x00; resp[p++] = 0x01;                   // TYPE = A
            resp[p++] = 0x00; resp[p++] = 0x01;                   // CLASS = IN
            resp[p++] = (byte)(ttlSeconds >> 24);
            resp[p++] = (byte)(ttlSeconds >> 16);
            resp[p++] = (byte)(ttlSeconds >> 8);
            resp[p++] = (byte)ttlSeconds;
            resp[p++] = 0x00; resp[p++] = 0x04;                   // RDLENGTH = 4
            resp[p++] = (byte)(answerAddress >> 24);
            resp[p++] = (byte)(answerAddress >> 16);
            resp[p++] = (byte)(answerAddress >> 8);
            resp[p++] = (byte)answerAddress;

            return resp;
        }

        /// <summary>把 DNS 应答翻译成"发回给客户端"的 UDP 报文</summary>
        public static byte[] BuildDnsReplyPacket(Ipv4Packet ip, UdpDatagram udp, byte[] dnsResponse)
        {
            return BuildUdp(
                ip.DestinationAddress, ip.SourceAddress,
                udp.DestinationPort, udp.SourcePort,
                dnsResponse);
        }
    }
}
