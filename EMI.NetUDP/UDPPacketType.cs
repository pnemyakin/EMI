namespace EMI.NetUDP
{
    /// <summary>
    /// Типы UDP пакетов протокола
    /// </summary>
    internal enum UDPPacketType : byte
    {
        /// <summary>
        /// Данные без гарантии доставки: [type:1][data:N]
        /// </summary>
        UnreliableData = 1,

        /// <summary>
        /// Данные с гарантией доставки: [type:1][seq:2][data:N]
        /// </summary>
        ReliableData = 2,

        /// <summary>
        /// Фрагмент без гарантии: [type:1][fragId:2][fragIdx:1][fragCount:1][data:N]
        /// </summary>
        UnreliableFragment = 3,

        /// <summary>
        /// Фрагмент с гарантией: [type:1][seq:2][fragId:2][fragIdx:1][fragCount:1][data:N]
        /// </summary>
        ReliableFragment = 4,

        /// <summary>
        /// Подтверждение получения: [type:1][ackSeq:2][ackBits:4]
        /// </summary>
        Ack = 5,

        /// <summary>
        /// Запрос подключения: [type:1][token:8]
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
        /// Пинг: [type:1][pingId:2]
        /// </summary>
        Ping = 13,

        /// <summary>
        /// Понг (ответ на пинг): [type:1][pingId:2]
        /// </summary>
        Pong = 14,
    }
}
