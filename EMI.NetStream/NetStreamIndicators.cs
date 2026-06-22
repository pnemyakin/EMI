using EMI.Indicators;

namespace EMI.NetStream
{
    using Structures;

    /// <summary>
    /// RPC-индикаторы для удалённых операций над потоком.
    /// </summary>
    internal class NetStreamIndicators
    {
        public readonly Indicator.FuncOut<NetStreamInfo> GetStreamInfo;
        public readonly Indicator.FuncOut<long> GetStreamLength;
        public readonly Indicator.FuncOut<long> GetStreamPosition;
        public readonly Indicator.FuncOut<bool, long> SetStreamPosition;

        public readonly Indicator.FuncOut<FlushInfo> Flush;
        public readonly Indicator.FuncOut<ReadInfo, ReadInInfo> Read;
        public readonly Indicator.FuncOut<SeekInfo, SeekInInfo> Seek;
        public readonly Indicator.FuncOut<bool, long> SetLength;
        public readonly Indicator.FuncOut<WriteInfo, WriteInInfo> Write;
        public readonly Indicator.Func Close;

        public NetStreamIndicators(int id)
        {
            GetStreamInfo = new Indicator.FuncOut<NetStreamInfo>("NetStream_GetStreamInfo#" + id);
            GetStreamLength = new Indicator.FuncOut<long>("NetStream_GetStreamLength#" + id);
            GetStreamPosition = new Indicator.FuncOut<long>("NetStream_GetStreamPosition#" + id);
            SetStreamPosition = new Indicator.FuncOut<bool, long>("NetStream_SetStreamPosition#" + id);

            Flush = new Indicator.FuncOut<FlushInfo>("NetStream_Flush#" + id);
            Read = new Indicator.FuncOut<ReadInfo, ReadInInfo>("NetStream_Read#" + id);
            Seek = new Indicator.FuncOut<SeekInfo, SeekInInfo>("NetStream_Seek#" + id);
            SetLength = new Indicator.FuncOut<bool, long>("NetStream_SetLength#" + id);
            Write = new Indicator.FuncOut<WriteInfo, WriteInInfo>("NetStream_Write#" + id);
            Close = new Indicator.Func("NetStream_Close#" + id);
        }
    }
}
