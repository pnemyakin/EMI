namespace EMI.NetUDPv2
{
    /// <summary>
    /// Константы UDP v2 протокола.
    /// Оптимизирован для real-time приложений, игр и работы при несовершенном соединении.
    /// </summary>
    public static class V2Constants
    {
        /// <summary>
        /// Версия протокола (для проверки совместимости при handshake)
        /// </summary>
        public const ushort ProtocolVersion = 2;

        /// <summary>
        /// Безопасный MTU по умолчанию (IPv4 + UDP header = 28 байт).
        /// Для LAN/гигабита рекомендуется установить MTU через NetUDPv2Client.SetMTU() в 8192+
        /// </summary>
        public const int DefaultMTU = 1400;

        /// <summary>
        /// Минимальный допустимый MTU
        /// </summary>
        public const int MinMTU = 576;

        /// <summary>
        /// Максимальный MTU (UDP payload limit)
        /// </summary>
        public const int MaxMTU = 65507;

        /// <summary>
        /// Максимальный размер данных в ненадёжном пакете при DefaultMTU (MTU - 1 байт type)
        /// </summary>
        public const int MaxUnreliablePayload = DefaultMTU - 1;

        /// <summary>
        /// Максимальный размер данных в надёжном пакете при DefaultMTU (MTU - 5 байт: type + seq)
        /// </summary>
        public const int MaxReliablePayload = DefaultMTU - V2Headers.ReliableHeaderSize;

        /// <summary>
        /// Максимальный размер данных в ненадёжном фрагменте при DefaultMTU (MTU - 1 type - 8 frag header)
        /// </summary>
        public const int MaxUnreliableFragmentPayload = DefaultMTU - 1 - V2Headers.FragmentHeaderSize;

        /// <summary>
        /// Максимальный размер данных в надёжном фрагменте при DefaultMTU (MTU - 5 reliable - 8 frag)
        /// </summary>
        public const int MaxReliableFragmentPayload = DefaultMTU - V2Headers.ReliableHeaderSize - V2Headers.FragmentHeaderSize;

        /// <summary>
        /// Вычисляет MaxUnreliablePayload для заданного MTU
        /// </summary>
        public static int CalcMaxUnreliablePayload(int mtu) => mtu - 1;

        /// <summary>
        /// Вычисляет MaxReliablePayload для заданного MTU
        /// </summary>
        public static int CalcMaxReliablePayload(int mtu) => mtu - V2Headers.ReliableHeaderSize;

        /// <summary>
        /// Вычисляет MaxUnreliableFragmentPayload для заданного MTU
        /// </summary>
        public static int CalcMaxUnreliableFragmentPayload(int mtu) => mtu - 1 - V2Headers.FragmentHeaderSize;

        /// <summary>
        /// Вычисляет MaxReliableFragmentPayload для заданного MTU
        /// </summary>
        public static int CalcMaxReliableFragmentPayload(int mtu) => mtu - V2Headers.ReliableHeaderSize - V2Headers.FragmentHeaderSize;

        /// <summary>
        /// Максимальное число фрагментов на сообщение (ushort max)
        /// </summary>
        public const int MaxFragmentCount = 65535;

        /// <summary>
        /// Размер битового поля ACK (64 пакета через ulong)
        /// </summary>
        public const int AckBitfieldSize = 64;

        /// <summary>
        /// Максимальное кол-во SACK-диапазонов в одном ACK-пакете.
        /// 64: при 4MB чанке (~3000 фрагментов) и 5% потерь — до 150 дыр.
        /// ACK = 14 + 64*8 = 526 байт — в пределах MTU 1400.
        /// </summary>
        public const int MaxSackRanges = 64;

        // ── Congestion Control ─────────────────────────────────────────────────

        /// <summary>
        /// Начальный CWND (congestion window) в пакетах.
        /// 32 — мягкий старт для интернет-каналов: меньше начальный burst, стабильнее на lossy-линках.
        /// До целевого окна (~250 пакетов при 50 Мбит/50 мс RTT) достигает за 2 RTT.
        /// </summary>
        public const int InitialCwnd = 64;

        /// <summary>
        /// Минимальный CWND (не даём упасть ниже).
        /// 32 пакета = ~44КБ при MTU 1400 — не даём пропускной способности схлопнуться.
        /// </summary>
        public const int MinCwnd = 32;

        /// <summary>
        /// Максимальный CWND (верхний предел). Увеличен для гигабитных соединений.
        /// </summary>
        public const int MaxCwnd = 16384;

        /// <summary>
        /// Множитель при обнаружении потерь (multiplicative decrease).
        /// 0.7 = мягче чем TCP (0.5), быстрее восстановление после потери.
        /// </summary>
        public const float CongestionDecreaseMultiplier = 0.7f;

        /// <summary>
        /// Аддитивное увеличение CWND при успешной доставке (пакетов за RTT).
        /// 4 = быстрее восстановление в congestion avoidance после потерь.
        /// </summary>
        public const float CongestionIncreasePerRtt = 4.0f;

        /// <summary>
        /// Минимальный интервал между congestion decrease событиями (мс).
        /// Фактический интервал = max(это значение, SmoothedRtt) — не меньше 1 RTT.
        /// 1000ms — при burst-потере маршрутизатор роняет 2-3 пакета одновременно,
        /// но их RTO истекает с разбросом 200-900мс. При 200мс каждый срабатывает
        /// как отдельный CC DECREASE → Cwnd × 0.7^3 = 0.34× и SsThresh каскадно
        /// рушится. 1000мс объединяет весь burst в одно событие.
        /// </summary>
        public const int CongestionDecreaseIntervalMs = 1000;

        // ── Retransmission ─────────────────────────────────────────────────────

        /// <summary>
        /// Начальный RTO (retransmission timeout) в мс, до первого RTT-измерения
        /// </summary>
        public const int InitialRtoMs = 200;

        /// <summary>
        /// Минимальный RTO (мс)
        /// </summary>
        public const int MinRtoMs = 50;

        /// <summary>
        /// Максимальный RTO (мс). Увеличен для устойчивости при тяжёлых RPC.
        /// </summary>
        public const int MaxRtoMs = 5000;

        /// <summary>
        /// Множитель для fast-retransmit: считаем пакет потерянным после получения 
        /// этого количества ACK-ов для более поздних пакетов (аналог TCP triple dup-ACK)
        /// </summary>
        public const int FastRetransmitDupAckThreshold = 3;

        /// <summary>
        /// Максимальное кол-во попыток ретрансмиссии перед отключением.
        /// 40 попыток ≈ 200+400+800+1600+3200+5000*35 ≈ 3 минуты при максимальном backoff.
        /// </summary>
        public const int MaxRetransmitAttempts = 40;

        /// <summary>
        /// Максимальное время жизни надёжного пакета с момента первой отправки (мс).
        /// Если пакет не подтверждён за это время — соединение разрывается.
        /// Это дополнительная защита: даже если MaxRetransmitAttempts не достигнут 
        /// (например, из-за высокого RTO), но прошло слишком много времени — disconnect.
        /// По умолчанию 2 минуты.
        /// </summary>
        public const int ReliablePacketLifetimeMs = 120_000;

        // ── Timing ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Интервал тика ретрансмиссии (мс).
        /// 10 мс — обеспечивает быстрый flush delayed ACK и своевременную ретрансмиссию.
        /// </summary>
        public const int RetransmitTickIntervalMs = 10;

        /// <summary>
        /// Интервал отправки ACK (мс). Delayed ACK: накапливаем до DelayedAckThreshold
        /// пакетов, но не дольше этого интервала.
        /// </summary>
        public const int AckFlushIntervalMs = 5;

        /// <summary>
        /// Delayed ACK: отправлять ACK после получения этого количества reliable пакетов.
        /// 16 — баланс между сокращением ACK-трафика и своевременной обратной связью для CWND.
        /// При Cwnd=64 отправитель получает 4 ACK за окно — достаточно для плавного pipeline.
        /// </summary>
        public const int DelayedAckThreshold = 16;

        /// <summary>
        /// Таймаут для незавершённых фрагментированных сообщений (мс).
        /// Увеличен чтобы пережить длительные ретрансмиссии при плохом соединении.
        /// </summary>
        public const int FragmentTimeoutMs = 60000;

        /// <summary>
        /// Интервал внутреннего пинга (мс)
        /// </summary>
        public const int PingIntervalMs = 1000;

        /// <summary>
        /// Таймаут соединения при отсутствии данных (мс).
        /// 120 секунд — достаточно для ретрансмиссий при lossy интернет-канале и тяжёлых RPC.
        /// При 30с (старое значение) шторм ретрансмиссий при 512KB-чанке мог убить соединение.
        /// </summary>
        public const int ConnectionTimeoutMs = 120000;

        /// <summary>
        /// Таймаут handshake при подключении (мс на одну попытку)
        /// </summary>
        public const int ConnectAttemptTimeoutMs = 500;

        /// <summary>
        /// Максимальное число попыток подключения
        /// </summary>
        public const int MaxConnectAttempts = 10;

        // ── Receive Queue ──────────────────────────────────────────────────────

        /// <summary>        /// Размер UDP-буфера сокета (send и receive) в байтах.
        /// Дефолт ОС ~64КБ — катастрофически мало при burst-загрузке.
        /// 16МБ позволяет буферизовать ~11400 пакетов по 1400б, снижая потери при burst.
        /// </summary>
        public const int SocketBufferSize = 16 * 1024 * 1024;

        /// <summary>        /// Размер очереди входящих пакетов
        /// </summary>
        public const int ReceiveQueueCapacity = 8192;

        /// <summary>
        /// Максимальное число ожидающих подтверждения пакетов в окне
        /// </summary>
        public const int MaxPendingReliablePackets = 16384;

        // ── NACK ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Через сколько мс после обнаружения дыры слать NACK
        /// (даём время ACK-у прийти естественным путём)
        /// </summary>
        public const int NackDelayMs = 50;

        /// <summary>
        /// Максимальное количество seq в одном NACK-пакете
        /// </summary>
        public const int MaxNackSequences = 64;
    }
}
