namespace EMI.Network
{
    /// <summary>
    /// Конфигурация двухуровневого ограничения частоты RPC-вызовов.
    /// <para>
    /// Soft-уровень: при исчерпании токенов пакеты молча отбрасываются (без отключения).
    /// Hard-уровень: при устойчивом превышении (исчерпании hard-бюджета) клиент отключается.
    /// </para>
    /// <para>
    /// Установить на <see cref="Server.RateLimit"/> перед <see cref="Server.Start"/>.
    /// Передать null для полного отключения ограничений (по умолчанию).
    /// </para>
    /// </summary>
    public sealed class RateLimitConfig
    {
        /// <summary>
        /// Максимальное количество soft-токенов (burst-бюджет).
        /// Определяет допустимый пиковый всплеск RPC-вызовов.
        /// <para>По умолчанию: 120</para>
        /// </summary>
        public int SoftTokens { get; set; } = 120;

        /// <summary>
        /// Скорость восполнения soft-токенов (токенов в секунду).
        /// Определяет допустимую среднюю частоту RPC-вызовов.
        /// <para>По умолчанию: 60</para>
        /// </summary>
        public double SoftRefillRate { get; set; } = 60;

        /// <summary>
        /// Максимальное количество hard-токенов.
        /// Каждый soft-дроп тратит один hard-токен.
        /// При исчерпании — клиент отключается.
        /// <para>По умолчанию: 50</para>
        /// </summary>
        public int HardTokens { get; set; } = 50;

        /// <summary>
        /// Скорость восполнения hard-токенов (токенов в секунду).
        /// Медленное восполнение: единичные всплески прощаются, устойчивый флуд — нет.
        /// <para>По умолчанию: 1</para>
        /// </summary>
        public double HardRefillRate { get; set; } = 1;

        /// <summary>
        /// Создаёт копию конфигурации (для передачи каждому клиенту).
        /// </summary>
        internal RateLimitConfig Clone()
        {
            return new RateLimitConfig
            {
                SoftTokens = SoftTokens,
                SoftRefillRate = SoftRefillRate,
                HardTokens = HardTokens,
                HardRefillRate = HardRefillRate,
            };
        }
    }
}
