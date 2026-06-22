using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SmartPackager;

namespace EMI
{
    internal static class DPack
    {
        public static Packager.M<DateTime> DPing = Packager.Create<DateTime>();
        public const int sizeof_DPing = 8;

        /// <summary>
        /// Формат простого RPC-вызова (без ReturnWait): только method ID (64-bit)
        /// </summary>
        public static Packager.M<long> DRPC = Packager.Create<long>();
        public const int sizeof_DRPC = 8;

        /// <summary>
        /// Формат RPC_Return запроса: method ID (64-bit) + call ID (32-bit)
        /// </summary>
        public static Packager.M<long, int> DRPCReturn = Packager.Create<long, int>();
        public const int sizeof_DRPCReturn = 12;

        /// <summary>
        /// Формат RPC_Returned ответа: только call ID (32-bit)
        /// </summary>
        public static Packager.M<int> DRPCReturned = Packager.Create<int>();
        public const int sizeof_DRPCReturned = 4;

        public static Packager.M<bool, long> DForwarding = Packager.Create<bool, long>();
        public const int sizeof_DForwarding = 9;
    }
}
