using System.IO;
using System.Runtime.InteropServices;

namespace EMI.NetStream.Structures
{
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    internal struct SeekInInfo
    {
        [FieldOffset(0)]
        public long Offset;
        [FieldOffset(8)]
        public SeekOrigin Origin;

        public SeekInInfo(long offset, SeekOrigin origin)
        {
            Offset = offset;
            Origin = origin;
        }
    }
}
