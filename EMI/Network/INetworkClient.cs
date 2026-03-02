using System.Threading;
using System.Threading.Tasks;
using System;

namespace EMI.Network
{
    using NGC;
    /* Рекомендации по реализации интерфейса
    * address для сетевых подключений в виде текста localhost#port
    * address для других типов интерфейсов может быть описан свой
    * при неверном address выдовать исключение с пояснительной ошибкой
    * следует использовать многопоточность/асинхронность
    * не асинхронные методы не могут блокировать поток
    * данный интерфейс является узким местом и поэтому должн максимально оптимизированн (ИЗБЕГАТЬ создания объектов)
    * при использовании сетевых сокетов и подобных проверять от того ли адреса приходят данные (для функции Accept) - посторонние данные отбрасывать
    * Disconnect по возможности должен собеседнику сообщать об отключении
    * Send игнорирует свойство offset массива и начинает с нуля
    */
    /// <summary>
    /// Делегат для INetworkClient - вызывается когда клиент был отключен
    /// </summary>
    /// <param name="error">код/сообщение/текст ошибки</param>
    public delegate void INetworkClientDisconnected(string error);

    /// <summary>
    /// Интерфейс для передачи данных между клиентом и сервером
    /// </summary>
    public interface INetworkClient
    {
        /// <summary>
        /// Подключён ли клиент
        /// </summary>
        bool IsConnect { get; }
        /// <summary>
        /// Сколько байт в секунду отправляется
        /// </summary>
        int SendByteSpeed { get; }
        /// <summary>
        /// Число от 0 до 1 (сколько % байт доставленно) [если 0 то сеть сильно перегружена]
        /// </summary>
        float DeliveredRate { get; }
        /// <summary>
        /// Какие пакеты можно игнорировать при перезгрузке сети
        /// </summary>
        RandomDropType RandomDrop { get; set; }
        /// <summary>
        /// Вызывается при отключении клиента из за внешней ошибки
        /// </summary>
        event INetworkClientDisconnected Disconnected;
        /// <summary>
        /// Подключиться к серверу
        /// </summary>
        /// <param name="address">адрес сервера</param>
        /// <returns>было ли произведено подключение</returns>
        /// <param name="token">токен отмены задачи</param>
        Task<bool> Connect(string address, CancellationToken token);
        /// <summary>
        /// Отключиться от сервера
        /// <param name="user_error">ошибка/сообщение отключения</param>
        /// </summary>
        void Disconnect(string user_error);
        /// <summary>
        /// Отправить данные на сервер
        /// </summary>
        /// <param name="array">массив байт для отправки</param>
        /// <param name="guaranteed">гарантированная доставка</param>
        /// <param name="token">токен отмены</param>
        /// <returns></returns>
        Task Send(INGCArray array, bool guaranteed, CancellationToken token);
        /// <summary>
        /// Считывает пакет
        /// </summary>
        /// <param name="max_size">максимальный размер пакета для обработки (если больше то клиент будет отключен)</param>
        /// <param name="token">токен отмены операции</param>
        Task<INGCArray> AcceptPacket(int max_size, CancellationToken token);
        /// <summary>
        /// Возвращает адресс с которым связан данных клиент
        /// </summary>
        /// <returns></returns>
        string GetRemoteClientAddress();
    }
}
