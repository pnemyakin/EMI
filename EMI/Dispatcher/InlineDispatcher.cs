using System;

namespace EMI
{
    /// <summary>
    /// Исполняет RPC-вызов синхронно, прямо на потоке приёма пакетов (без переброски).
    /// Строго последовательный порядок, ноль накладных расходов на планирование.
    /// <para>
    /// Подходит, когда обработчики короткие, а лишние потоки не нужны. НЕ используйте с
    /// долгими/блокирующими обработчиками: они задержат чтение следующих пакетов.
    /// </para>
    /// </summary>
    public sealed class InlineDispatcher : IRpcDispatcher
    {
        /// <summary>Общий экземпляр без состояния — можно переиспользовать.</summary>
        public static readonly InlineDispatcher Instance = new InlineDispatcher();

        /// <inheritdoc/>
        public void Post(Action call) => call();
    }
}
