using System;
using System.Threading.Tasks;

namespace EMI
{
    /// <summary>
    /// Диспетчер по умолчанию: каждый RPC-вызов исполняется на пуле потоков (<see cref="Task.Run(Action)"/>).
    /// Максимальный параллелизм, порядок исполнения НЕ гарантируется.
    /// Поведение идентично тому, что EMI делал до появления сменяемых диспетчеров.
    /// </summary>
    public sealed class ThreadPoolDispatcher : IRpcDispatcher
    {
        /// <summary>Общий экземпляр без состояния — можно переиспользовать.</summary>
        public static readonly ThreadPoolDispatcher Instance = new ThreadPoolDispatcher();

        /// <inheritdoc/>
        public void Post(Action call) => Task.Run(call);
    }
}
