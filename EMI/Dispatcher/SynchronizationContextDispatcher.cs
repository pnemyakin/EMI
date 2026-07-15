using System;
using System.Threading;

namespace EMI
{
    /// <summary>
    /// Перебрасывает RPC-вызовы в захваченный <see cref="SynchronizationContext"/> — обычно
    /// контекст главного потока (Unity main thread, WPF/WinForms UI-поток). В отличие от
    /// <see cref="PumpDispatcher"/>, ручной <c>Pump()</c> не нужен: доставку обеспечивает сам контекст.
    /// </summary>
    /// <remarks>
    /// Создавайте экземпляр В ТОМ потоке, в котором должны исполняться обработчики
    /// (тогда сработает <see cref="SynchronizationContext.Current"/>), либо передайте контекст явно.
    /// Порядок исполнения — FIFO в пределах гарантий самого контекста.
    /// </remarks>
    public sealed class SynchronizationContextDispatcher : IRpcDispatcher
    {
        private readonly SynchronizationContext _context;
        private readonly SendOrPostCallback _trampoline = static state => ((Action)state)();

        /// <summary>
        /// Захватывает <see cref="SynchronizationContext.Current"/> вызывающего потока.
        /// </summary>
        /// <exception cref="InvalidOperationException">у текущего потока нет SynchronizationContext</exception>
        public SynchronizationContextDispatcher()
            : this(SynchronizationContext.Current ?? throw new InvalidOperationException(
                "No SynchronizationContext on the current thread. Create this dispatcher on the main/UI thread, " +
                "or pass a context explicitly."))
        {
        }

        /// <summary>
        /// Использует переданный контекст.
        /// </summary>
        /// <param name="context">контекст, в который постятся вызовы</param>
        public SynchronizationContextDispatcher(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <inheritdoc/>
        public void Post(Action call) => _context.Post(_trampoline, call);
    }
}
