namespace EMI.NetStream.Structures
{
    internal struct ReadInInfo
    {
        /// <summary>
        /// Размер буфера на стороне вызывающего (информационное поле).
        /// </summary>
        public int BufferSize;
        /// <summary>
        /// Смещение в буфере вызывающего (не используется хостом, но сохраняется для контракта).
        /// </summary>
        public int Offset;
        /// <summary>
        /// Количество байт для чтения.
        /// </summary>
        public int Count;

        public ReadInInfo(int bufferSize, int offset, int count)
        {
            BufferSize = bufferSize;
            Offset = offset;
            Count = count;
        }
    }
}
