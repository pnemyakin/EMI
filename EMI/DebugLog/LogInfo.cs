namespace EMI.DebugLog
{
    /// <summary>
    /// Тип сообщения
    /// </summary>
    public enum LogType:byte
    {
        /// <summary>
        /// Просто сообщение
        /// </summary>
        Message,
        /// <summary>
        /// Предупреждение
        /// </summary>
        Warning,
        /// <summary>
        /// Ошибка не вызывающие отключение
        /// </summary>
        Error,
        /// <summary>
        /// Ошибка приводящия к отключению клиента
        /// </summary>
        CriticalError
    }
}
