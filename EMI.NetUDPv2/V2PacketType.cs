namespace EMI.NetUDPv2
{
    /// <summary>
    /// Типы пакетов протокола UDPv2
    /// </summary>
    internal enum V2PacketType : byte
    {
        /// <summary>
        /// Данные без гарантии доставки (один пакет): [type:1][data:N]
        /// </summary>
        UnreliableData = 1,

        /// <summary>
        /// Данные с гарантией доставки (один пакет): [type:1][seq:4][data:N]
        /// </summary>
        ReliableData = 2,

        /// <summary>
        /// Фрагмент без гарантии: [type:1][msgId:4][fragIdx:2][fragCount:2][data:N]
        /// </summary>
        UnreliableFragment = 3,

        /// <summary>
        /// Фрагмент с гарантией: [type:1][seq:4][msgId:4][fragIdx:2][fragCount:2][data:N]
        /// Каждый фрагмент имеет свой sequence и подтверждается отдельно.
        /// При потере ретрансмитится ТОЛЬКО потерянный фрагмент.
        /// </summary>
        ReliableFragment = 4,

        /// <summary>
        /// Подтверждение с SACK: [type:1][ackSeq:4][ackBits:8][sackCount:1][sackRanges:N*8]
        /// ackBits подтверждает 64 пакета перед ackSeq.
        /// sackRanges — дополнительные диапазоны [start:4][end:4] для точной информации о доставке.
        /// </summary>
        Ack = 5,

        /// <summary>
        /// Запрос подключения: [type:1][protocolVersion:2][token:8]
        /// </summary>
        ConnectionRequest = 10,

        /// <summary>
        /// Подтверждение подключения: [type:1][token:8]
        /// </summary>
        ConnectionAccept = 11,

        /// <summary>
        /// Отключение: [type:1][reason:N]
        /// </summary>
        Disconnect = 12,

        /// <summary>
        /// Пинг для RTT: [type:1][pingId:2][timestamp:8]
        /// Включает timestamp для точного RTT без накладных расходов
        /// </summary>
        Ping = 13,

        /// <summary>
        /// Понг: [type:1][pingId:2][echoTimestamp:8]
        /// </summary>
        Pong = 14,

        /// <summary>
        /// Selective NACK — явный запрос ретрансмиссии конкретных seq: [type:1][count:2][seqs:N*4]
        /// Получатель может послать NACK если обнаружил дыру в последовательности.
        /// </summary>
        Nack = 15,
    }
}
