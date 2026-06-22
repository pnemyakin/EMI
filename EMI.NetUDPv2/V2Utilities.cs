using System;
using System.Net;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// Утилиты UDPv2 протокола
    /// </summary>
    internal static class V2Utilities
    {
        /// <summary>
        /// Парсит адрес формата "host#port" в IPEndPoint
        /// </summary>
        public static IPEndPoint ParseIPAddress(string address)
        {
            string[] parts = address.Split('#');
            string host = parts[0].ToLower().Trim();
            IPAddress ip;
            switch (host)
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
                    ip = IPAddress.Parse(parts[0].ToUpper());
                    break;
            }
            return new IPEndPoint(ip, ushort.Parse(parts[1].Trim()));
        }
    }

    /// <summary>
    /// Struct-ключ для словаря клиентов по IPEndPoint (без аллокаций при поиске)
    /// </summary>
    internal readonly struct EndPointKey : IEquatable<EndPointKey>
    {
        private readonly long AddrLow;
        private readonly long AddrHigh;
        private readonly int Port;

        public EndPointKey(IPEndPoint ep)
        {
            Port = ep.Port;
            byte[] bytes = ep.Address.GetAddressBytes();
            AddrLow = 0;
            AddrHigh = 0;

            if (bytes.Length <= 8)
            {
                for (int i = 0; i < bytes.Length; i++)
                    AddrLow |= ((long)bytes[i]) << (i * 8);
            }
            else
            {
                for (int i = 0; i < 8 && i < bytes.Length; i++)
                    AddrLow |= ((long)bytes[i]) << (i * 8);
                for (int i = 8; i < bytes.Length; i++)
                    AddrHigh |= ((long)bytes[i]) << ((i - 8) * 8);
            }
        }

        public bool Equals(EndPointKey other) =>
            AddrLow == other.AddrLow && AddrHigh == other.AddrHigh && Port == other.Port;

        public override bool Equals(object obj) => obj is EndPointKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = AddrLow.GetHashCode();
                hash = hash * 397 ^ AddrHigh.GetHashCode();
                hash = hash * 397 ^ Port;
                return hash;
            }
        }
    }
}
