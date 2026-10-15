using System.Net;
using System.Net.Sockets;

namespace Tongyin.Infrastructure.Ai;

/// <summary>
/// 限制 AI 生成图片下载时的实际网络连接目标，阻断 DNS 解析到回环、内网和保留地址。
/// </summary>
public static class PublicImageNetworkPolicy
{
    /// <summary>
    /// 检查解析出来的 IP 是否为可连接的公网地址；IPv4 映射的 IPv6 地址按 IPv4 判断。
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicAddress(address.MapToIPv4());
        }

        var octets = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = octets[0];
            var second = octets[1];
            return first is not (0 or 10 or 127) && first < 224
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 168)
                && !(first == 192 && second == 0 && octets[2] is 0 or 2)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && octets[2] == 100)
                && !(first == 203 && second == 0 && octets[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        // 仅允许全局单播 2000::/3；拒绝 6to4 和文档示例地址。
        return octets[0] is >= 0x20 and <= 0x3F
            && !(octets[0] == 0x20 && octets[1] == 0x02)
            && !(octets[0] == 0x20 && octets[1] == 0x01 && octets[2] == 0x0D && octets[3] == 0xB8)
            && !address.IsIPv6Teredo;
    }

    /// <summary>
    /// 自行解析 DNS 并只连接校验通过的 IP，避免只校验域名而被 DNS 重绑定绕过。
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        foreach (var address in addresses.Where(IsPublicAddress))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("生成图片下载地址没有可用的公网连接");
    }
}
