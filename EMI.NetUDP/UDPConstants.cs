namespace EMI.NetUDP
{
    /// <summary>
    /// Константы UDP протокола
    /// </summary>
    public static class UDPConstants
    {
        /// <summary>
        /// Безопасный MTU для интернета (подходит для большинства сетей)
        /// </summary>
        public const int MTU = 1200;

        /// <summary>
        /// Максимальный размер данных в ненадёжном пакете (MTU - 1 байт type)
        /// </summary>
        public const int MaxUnreliablePayload = MTU - 1;

        /// <summary>
        /// Максимальный размер данных в надёжном пакете (MTU - 3 байта header)
        /// </summary>
        public const int MaxReliablePayload = MTU - UDPReliableHeader.SizeOf;

        /// <summary>
        /// Максимальный размер данных в ненадёжном фрагменте (MTU - 1 type - 4 frag header)
        /// </summary>
        public const int MaxUnreliableFragmentPayload = MTU - 1 - UDPFragmentHeader.SizeOf;

        /// <summary>
        /// Максимальный размер данных в надёжном фрагменте (MTU - 3 header - 4 frag header)
        /// </summary>
        public const int MaxReliableFragmentPayload = MTU - UDPReliableHeader.SizeOf - UDPFragmentHeader.SizeOf;

        /// <summary>
        /// Максимальное число фрагментов (ограничение ushort)
        /// </summary>
        public const int MaxFragmentCount = 65535;

        /// <summary>
        /// Начальный интервал ретрансляции для надёжных пакетов (мс)
        /// </summary>
        public const int InitialRetransmitIntervalMs = 100;

        /// <summary>
        /// Максимальный интервал ретрансляции (мс)
        /// </summary>
        public const int MaxRetransmitIntervalMs = 1000;

        /// <summary>
        /// Максимальное кол-во попыток ретрансляции перед отключением
        /// </summary>
        public const int MaxRetransmitAttempts = 15;

        /// <summary>
        /// Таймаут для незавершённых фрагментированных сообщений (мс)
        /// </summary>
        public const int FragmentTimeoutMs = 10000;

        /// <summary>
        /// Интервал внутреннего пинга для обнаружения обрыва (мс)
        /// </summary>
        public const int InternalPingIntervalMs = 1000;

        /// <summary>
        /// Таймаут при отсутствии данных от удалённой стороны (мс)
        /// </summary>
        public const int ConnectionTimeoutMs = 10000;

        /// <summary>
        /// Максимальное число ожидающих подтверждения пакетов
        /// </summary>
        public const int MaxPendingReliablePackets = 256;

        /// <summary>
        /// Размер очереди входящих пакетов
        /// </summary>
        public const int ReceiveQueueCapacity = 1024;

        /// <summary>
        /// Размер окна для ACK-битового поля (32 пакета)
        /// </summary>
        public const int AckBitfieldSize = 32;
    }
}
