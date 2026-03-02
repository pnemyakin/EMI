using System.Runtime.CompilerServices;

namespace EMI.Network
{
    /// <summary>
    /// Двухуровневый Token Bucket ограничитель частоты RPC-вызовов.
    /// <para>
    /// Soft-уровень: при исчерпании soft-токенов пакет отбрасывается (дроп), 
    /// расходуется один hard-токен. Burst-трафик допускается в рамках soft-бюджета.
    /// </para>
    /// <para>
    /// Hard-уровень: при исчерпании hard-токенов возвращается <see cref="RateLimitResult.Kick"/> — 
    /// клиент должен быть отключён (устойчивый флуд).
    /// </para>
    /// <para>
    /// Потокобезопасность: класс НЕ потокобезопасен. Каждый <see cref="Client"/> владеет своим экземпляром,
    /// и вызовы <see cref="TryConsume"/> происходят последовательно из InputStack.Pop().
    /// </para>
    /// </summary>
    internal sealed class RpcRateLimiter
    {
        private readonly int _softMax;
        private readonly double _softRefillRate;
        private readonly int _hardMax;
        private readonly double _hardRefillRate;

        private double _softTokens;
        private double _hardTokens;
        private long _lastTicks;

        /// <summary>
        /// Количество пакетов, отброшенных soft-лимитом (для диагностики).
        /// </summary>
        public long SoftDropCount { get; private set; }

        /// <summary>
        /// Текущее количество soft-токенов (для диагностики/тестов).
        /// </summary>
        internal double CurrentSoftTokens => _softTokens;

        /// <summary>
        /// Текущее количество hard-токенов (для диагностики/тестов).
        /// </summary>
        internal double CurrentHardTokens => _hardTokens;

        /// <summary>
        /// Создаёт новый ограничитель из конфигурации.
        /// </summary>
        /// <param name="config">Параметры ограничения. Не должен быть null.</param>
        public RpcRateLimiter(RateLimitConfig config)
        {
            _softMax = config.SoftTokens;
            _softRefillRate = config.SoftRefillRate;
            _hardMax = config.HardTokens;
            _hardRefillRate = config.HardRefillRate;

            _softTokens = _softMax;
            _hardTokens = _hardMax;
            _lastTicks = TickTime.Now.Ticks;
        }

        /// <summary>
        /// Пытается принять один RPC-пакет.
        /// </summary>
        /// <returns>
        /// <see cref="RateLimitResult.Accept"/> — пакет принят;
        /// <see cref="RateLimitResult.SoftDrop"/> — пакет отброшен (soft), клиент продолжает работать;
        /// <see cref="RateLimitResult.Kick"/> — клиент должен быть отключён (hard-бюджет исчерпан).
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RateLimitResult TryConsume()
        {
            Refill();

            // Soft: есть токены — принимаем
            if (_softTokens >= 1.0)
            {
                _softTokens -= 1.0;
                return RateLimitResult.Accept;
            }

            // Soft-дроп: расходуем hard-токен
            SoftDropCount++;

            if (_hardTokens >= 1.0)
            {
                _hardTokens -= 1.0;
                return RateLimitResult.SoftDrop;
            }

            // Hard-бюджет исчерпан
            return RateLimitResult.Kick;
        }

        /// <summary>
        /// Восполняет токены обоих уровней пропорционально прошедшему времени.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Refill()
        {
            long now = TickTime.Now.Ticks;
            long elapsed = now - _lastTicks;
            if (elapsed <= 0)
                return;

            _lastTicks = now;

            // TickTime использует Stopwatch.GetTimestamp() → ticks = частота Stopwatch
            // Преобразуем в секунды
            double seconds = (double)elapsed / System.Diagnostics.Stopwatch.Frequency;

            // Soft refill
            double softAdd = seconds * _softRefillRate;
            _softTokens += softAdd;
            if (_softTokens > _softMax)
                _softTokens = _softMax;

            // Hard refill
            double hardAdd = seconds * _hardRefillRate;
            _hardTokens += hardAdd;
            if (_hardTokens > _hardMax)
                _hardTokens = _hardMax;
        }
    }

    /// <summary>
    /// Результат проверки ограничения частоты.
    /// </summary>
    internal enum RateLimitResult : byte
    {
        /// <summary>
        /// Пакет принят — обрабатывать нормально.
        /// </summary>
        Accept,

        /// <summary>
        /// Пакет отброшен мягким лимитом — не обрабатывать, клиент остаётся подключён.
        /// </summary>
        SoftDrop,

        /// <summary>
        /// Hard-бюджет исчерпан — клиент должен быть отключён (устойчивый флуд).
        /// </summary>
        Kick,
    }
}
