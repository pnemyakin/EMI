using System;
using System.Collections.Concurrent;

namespace EMI
{
    /// <summary>
    /// Что делать, когда очередь <see cref="PumpDispatcher"/> переполнена (<see cref="PumpDispatcher.MaxQueueDepth"/>).
    /// </summary>
    public enum PumpOverflowPolicy
    {
        /// <summary>Отбросить новый вызов (пакет теряется, соединение живёт). По умолчанию.</summary>
        DropNewest,
        /// <summary>Отбросить самый старый вызов в очереди, поставить новый.</summary>
        DropOldest,
        /// <summary>Игнорировать лимит и продолжать копить (риск роста памяти).</summary>
        Ignore,
    }

    /// <summary>
    /// Диспетчер для игровых движков (Unity и т.п.): складывает RPC-вызовы в потокобезопасную
    /// FIFO-очередь. Вызовы НЕ исполняются, пока вы не дёрнете <see cref="Pump"/> из своего
    /// главного цикла (например, в <c>Update()</c>). Гарантирует последовательный порядок
    /// и исполнение в потоке, вызвавшем <see cref="Pump"/>.
    /// </summary>
    /// <remarks>
    /// Пример (Unity):
    /// <code>
    /// var pump = new PumpDispatcher();
    /// client.Dispatcher = pump;
    /// // ...
    /// void Update() => pump.Pump();   // RPC-обработчики выполнятся здесь, в main-потоке
    /// </code>
    /// </remarks>
    public sealed class PumpDispatcher : IRpcDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        /// <summary>Максимальная глубина очереди; при превышении срабатывает <see cref="OverflowPolicy"/>. 0 = без лимита.</summary>
        public int MaxQueueDepth { get; }

        /// <summary>Политика при переполнении очереди.</summary>
        public PumpOverflowPolicy OverflowPolicy { get; }

        /// <summary>Сколько вызовов ожидает в очереди прямо сейчас.</summary>
        public int PendingCount => _queue.Count;

        /// <summary>
        /// Вызывается, когда обработчик бросил исключение (по умолчанию EMI логирует сам,
        /// но здесь можно перехватить для своей телеметрии).
        /// </summary>
        public event Action<Exception> OnHandlerError;

        /// <summary>
        /// </summary>
        /// <param name="maxQueueDepth">лимит глубины очереди (0 = без лимита)</param>
        /// <param name="overflowPolicy">что делать при переполнении</param>
        public PumpDispatcher(int maxQueueDepth = 0, PumpOverflowPolicy overflowPolicy = PumpOverflowPolicy.DropNewest)
        {
            if (maxQueueDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
            MaxQueueDepth = maxQueueDepth;
            OverflowPolicy = overflowPolicy;
        }

        /// <inheritdoc/>
        public void Post(Action call)
        {
            if (MaxQueueDepth > 0 && _queue.Count >= MaxQueueDepth)
            {
                switch (OverflowPolicy)
                {
                    case PumpOverflowPolicy.DropNewest:
                        return; // новый вызов отбрасывается
                    case PumpOverflowPolicy.DropOldest:
                        _queue.TryDequeue(out _); // освобождаем место под новый
                        break;
                    case PumpOverflowPolicy.Ignore:
                        break;
                }
            }
            _queue.Enqueue(call);
        }

        /// <summary>
        /// Исполнить все накопленные на текущий момент RPC-вызовы в потоке вызывающего.
        /// Обрабатывает только те вызовы, что уже были в очереди на момент вызова — новые,
        /// добавленные во время работы, достанутся следующему <see cref="Pump"/> (нет риска зациклиться).
        /// </summary>
        /// <returns>сколько вызовов исполнено</returns>
        public int Pump()
        {
            int snapshot = _queue.Count;
            int done = 0;
            while (done < snapshot && _queue.TryDequeue(out var call))
            {
                done++;
                try { call(); }
                catch (Exception e) { OnHandlerError?.Invoke(e); }
            }
            return done;
        }

        /// <summary>
        /// Исполнить не более <paramref name="max"/> вызовов за один заход (ограничение бюджета кадра).
        /// </summary>
        /// <param name="max">максимум вызовов; значения &lt;= 0 ничего не делают</param>
        /// <returns>сколько вызовов исполнено</returns>
        public int Pump(int max)
        {
            int done = 0;
            while (done < max && _queue.TryDequeue(out var call))
            {
                done++;
                try { call(); }
                catch (Exception e) { OnHandlerError?.Invoke(e); }
            }
            return done;
        }

        /// <summary>Отбросить все ожидающие вызовы без исполнения (например, при смене сцены).</summary>
        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
