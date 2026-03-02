using EMI.NGC;

namespace EMI.Network
{
    /// <summary>
    /// Интерфейс для промежуточной обработки пакетов (шифрование, сжатие и т.д.)
    /// Middleware применяются в цепочке: Send → [Compress → Encrypt] → Wire → [Decrypt → Decompress] → Accept
    /// </summary>
    public interface IPacketMiddleware
    {
        /// <summary>
        /// Уникальный идентификатор middleware (для проверки совместимости клиент-сервер)
        /// </summary>
        byte Id { get; }

        /// <summary>
        /// Имя middleware (для диагностики)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Версия middleware (для проверки совместимости)
        /// </summary>
        byte Version { get; }

        /// <summary>
        /// Обрабатывает исходящий пакет перед отправкой по сети.
        /// Реализация ДОЛЖНА использовать буферы из NGCArray пула для zero-alloc.
        /// Возвращённый массив будет Dispose после отправки вызывающим кодом.
        /// </summary>
        /// <param name="input">исходный пакет (caller отвечает за Dispose)</param>
        /// <returns>трансформированный пакет (caller Dispose)</returns>
        INGCArray ProcessOutgoing(INGCArray input);

        /// <summary>
        /// Обрабатывает входящий пакет после получения из сети.
        /// Реализация ДОЛЖНА использовать буферы из NGCArray пула для zero-alloc.
        /// Возвращённый массив будет Dispose после обработки вызывающим кодом.
        /// </summary>
        /// <param name="input">полученный пакет (caller отвечает за Dispose)</param>
        /// <returns>восстановленный пакет (caller Dispose)</returns>
        INGCArray ProcessIncoming(INGCArray input);
    }
}
