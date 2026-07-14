using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EMI
{
    using EMI.DebugLog;
    using MyException;
    using Network;
    using NGC;

    /// <summary>
    /// Сервер
    /// </summary>
    public class Server
    {
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова
        /// </summary>
        public RPC RPC { get; private set; } = new RPC();
        /// <summary>
        /// Запущен ли сервер
        /// </summary>
        public bool IsRun { get; private set; }
        /// <summary>
        /// [Устанавливается для всех подключённых клиентов] Максимальный размер пакета который может отправить удалённый пользователь за один раз (если размер будет превышен - клиент будет отключен)
        /// </summary>
        public int MaxPacketAcceptSize = 1024 * 1024 * 64; //64 мегабайт
        /// <summary>
        /// [Устанавливается для всех подключённых клиентов] Частота опроса пинга
        /// </summary>
        public TimeSpan PingPollingInterval = new TimeSpan(0, 0, 0, 15);
        /// <summary>
        /// [Устанавливается для новых клиентов] Какие пакеты можно игнорировать при перезгрузке сети
        /// </summary>
        public RandomDropType RandomDrop = RandomDropType.NoGuaranteed;

        private CancellationTokenSource CancellationTokenSource;
        private List<Client> Clients;
        /// <summary>
        /// Логи сервера и клиентов [только для DEBUG билда]
        /// </summary>
        public readonly Logger Logger = new Logger();
        private readonly INetworkService Service;
        private readonly INetworkServer LowServer;


#if DEBUG
        private Client[] GetClients()
        {
            if (Clients == null)
                return new Client[0];
            else
                return Clients.ToArray();
        }
#endif

        /// <summary>
        /// Список клиентов подключеных к серверу
        /// </summary>
        public Client[] ServerClients =>
#if DEBUG
            GetClients();
#else
            throw new NotSupportedException();
#endif
        /// <summary>
        /// Имя сервиса по которому осуществляется низкоуровневый обмен сообщениями
        /// </summary>
        public string ServiceName =>
#if DEBUG
            Service.ToString();
#else
            throw new NotSupportedException();
#endif
        /// <summary>
        /// Адресс по которому сервер слушает подключения
        /// </summary>
        public string Address;

#if DEBUG
        /// <summary>
        /// Происходит когда клиент подключился
        /// </summary>
        public event Action<Client> OnClientConnect;
        /// <summary>
        /// Происходит когда клиент отключается
        /// </summary>
        public event Action<Client> OnClientDisconnect;
#endif
        /// <summary>
        /// Вызывается раз в секунду для проверки пинга
        /// </summary>
        internal event Action PingSend;

        /// <summary>
        /// Цепочка middleware для всех новых клиентов (сжатие, шифрование).
        /// Устанавливать ДО Start(). Передать null для отключения (нулевой overhead).
        /// Порядок: compress → encrypt.
        /// Если используется UseEncryption/UseCompression — заполняется автоматически при Start().
        /// </summary>
        public IPacketMiddleware[] Middlewares { get; set; }

        /// <summary>
        /// Конфигурация ограничения частоты RPC-вызовов (защита от флуда).
        /// Устанавливать ДО Start(). null = без ограничений (по умолчанию).
        /// <para>
        /// Двухуровневая схема: soft-лимит дропает пакеты без отключения,
        /// hard-лимит кикает клиента при устойчивом злоупотреблении.
        /// </para>
        /// </summary>
        public RateLimitConfig RateLimit { get; set; }

        /// <summary>
        /// Включить AES-256-GCM шифрование. Ключ НЕ передаётся открытым текстом — его согласует
        /// <see cref="KeyExchange"/> (по умолчанию RSA key-transport). Устанавливать ДО Start().
        /// </summary>
        public bool UseEncryption { get; set; }

        /// <summary>
        /// Включить LZ4 сжатие.
        /// Устанавливать ДО Start().
        /// </summary>
        public bool UseCompression { get; set; }

        /// <summary>
        /// Стратегия согласования ключа шифрования (лестница уровней, см. docs).
        /// Если <see cref="UseEncryption"/>=true и здесь null — при Start() создаётся
        /// <see cref="Network.RsaKeyExchange.CreateServer()"/> (уровень 2: защита от прослушки,
        /// не от активного MITM). Для защиты от MITM задайте свою стратегию с фиксированной парой,
        /// а клиентам раздайте публичный ключ для пиннинга; для PSK — <see cref="Network.PreSharedKeyExchange"/>.
        /// </summary>
        public IKeyExchange KeyExchange { get; set; }

        /// <summary>
        /// Создаёт новый сервер
        /// </summary>
        /// <param name="service">интерфейс подключения</param>
        /// <exception cref="PlatformNotSupportedException">Платформа big-endian не поддерживается</exception>
        public Server(INetworkService service)
        {
            ThrowIfBigEndian();
            Service = service;
            LowServer = Service.GetNewServer();
            Logger.Log(Messages.InitServer, Service);
        }

        /// <summary>
        /// Проверяет что платформа little-endian. EMI использует native byte order в заголовках и SmartPackager,
        /// поэтому big-endian машины несовместимы по сетевому протоколу.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Платформа big-endian не поддерживается</exception>
        private static void ThrowIfBigEndian()
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException(
                    "EMI does not support big-endian platforms. " +
                    "Network protocol headers and SmartPackager serialize data in native (little-endian) byte order.");
        }

        /// <summary>
        /// Запускает сервер
        /// </summary>
        /// <param name="address">локальный адрес прослушивания</param>
        /// <exception cref="AlreadyException">сервер уже запущен</exception>
        public void Start(string address)
        {
            Address = address;
            lock (this)
            {
                if (IsRun)
                    throw new AlreadyException();
                PingSend = null;
                IsRun = true;
                Clients = new List<Client>();
                CancellationTokenSource = new CancellationTokenSource();

                // Стратегия обмена ключами: по умолчанию RSA (ключ не идёт открытым текстом).
                // RSA-пара создаётся один раз и живёт всё время работы сервера (клиенты могут её пиннить).
                if (UseEncryption && KeyExchange == null)
                    KeyExchange = Network.RsaKeyExchange.CreateServer();

                LowServer.StartServer(address);
                PingProcessStart();
            }
            Logger.Log(Messages.ServerStarted);
        }

        /// <summary>
        /// Останавливает сервер
        /// </summary>
        /// <exception cref="AlreadyException">Сервер уже остановлен</exception>
        public void Stop()
        {
            lock (this)
            {
                if (!IsRun)
                    throw new AlreadyException();
                IsRun = false;
                CancellationTokenSource.Cancel();
                lock (Clients)
                {
                    for (int i = 0; i < Clients.Count; i++)
                    {
                        try { Clients[i].Disconnect("Server is closed"); } catch { }
                    }
                }
            }
            Logger.Log(Messages.ServerStopped);
        }

        private void PingProcessStart()
        {
            Task.Factory.StartNew(async () =>
            {
                Logger.Log(Messages.ServerPingStarted);
                var token = CancellationTokenSource.Token;
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(PingPollingInterval).ConfigureAwait(false);
                    PingSend?.Invoke();
                }
                Logger.Log(Messages.ServerPingStopped);
            }, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Ожидает подключение игрока
        /// </summary>
        /// <returns>подключённый клиент или null если операция отменена</returns>
        /// <exception cref="Exception">Сервер не был запущен</exception>
        public async Task<Client> Accept()
        {
            CancellationToken token;
            lock (this)
            {
                if (!IsRun)
                {
                    throw new Exception("Server is not running!");
                }
                token = CancellationTokenSource.Token;
            }
            var LowClient = await LowServer.AcceptClient(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Client client = null;

            // Подготовка middleware для серверного клиента.
            // ВАЖНО: каждый клиент получает СВОИ экземпляры middleware (AesGcm имеет stateful nonce).
            INetworkClient clientToUse = LowClient;

            if (Middlewares != null && Middlewares.Length > 0)
            {
                // Продвинутый режим: middleware заданы вручную, ключ пользователь координирует сам.
                // Обмен ключами (handshake v3) не выполняется — прямая обёртка.
                clientToUse = new MiddlewareNetworkClient(LowClient, Middlewares);
            }
            else if (UseCompression || (KeyExchange != null && KeyExchange.ProducesKey))
            {
                // Авто-путь: договариваемся о флагах + обмене ключами (ключ НЕ идёт открытым текстом).
                var result = await MiddlewareNetworkClient.PerformServerHandshake(
                    LowClient, UseCompression, KeyExchange, token).ConfigureAwait(false);
                if (result.Error != null)
                {
                    LowClient.Disconnect(result.Error);
                    throw new InvalidOperationException(result.Error);
                }
                if (result.Middlewares != null && result.Middlewares.Length > 0)
                    clientToUse = new MiddlewareNetworkClient(LowClient, result.Middlewares);
            }

            client = new Client(clientToUse, RPC, this)
            {
                MaxPacketAcceptSize = MaxPacketAcceptSize,
                RandomDrop = RandomDrop,
            };

            var list = Clients;
            lock (list)
            {
                list.Add(client);
#if DEBUG
                OnClientConnect?.Invoke(client);
#endif
            }

            client.Disconnected += (_) =>
            {
                lock (list)
                {
                    list.Remove(client);
#if DEBUG
                    OnClientDisconnect?.Invoke(client);
#endif
                }
            };
            return client;
        }
    }
}