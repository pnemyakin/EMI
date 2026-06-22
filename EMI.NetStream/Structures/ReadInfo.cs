namespace EMI.NetStream.Structures
{
    internal struct ReadInfo
    {
        public bool Result;
        public int ReadLen;
        public byte[] Buffer;
        public long Length;
        public long Position;

        public ReadInfo(bool result, int readLen, byte[] buffer, long length, long position)
        {
            Result = result;
            ReadLen = readLen;
            Buffer = buffer;
            Length = length;
            Position = position;
        }
    }
}
