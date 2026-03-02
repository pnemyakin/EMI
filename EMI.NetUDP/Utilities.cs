using System.Net;

namespace EMI.NetUDP
{
    internal static class Utilities
    {
        /// <summary>
        /// Парсит адрес формата "host#port" в IPEndPoint
        /// </summary>
        public static IPEndPoint ParseIPAddress(string address)
        {
            string[] adds = address.Split('#');
            adds[0] = adds[0].ToLower().Trim();
            IPAddress ip;
            switch (adds[0])
            {
                case "any":
                    ip = IPAddress.Any;
                    break;
                case "ipv6any":
                    ip = IPAddress.IPv6Any;
                    break;
                case "localhost":
                    ip = IPAddress.Loopback;
                    break;
                case "ipv6localhost":
                    ip = IPAddress.IPv6Loopback;
                    break;
                default:
                    ip = IPAddress.Parse(adds[0].ToUpper());
                    break;
            }
            return new IPEndPoint(ip, ushort.Parse(adds[1].Trim()));
        }
    }
}
