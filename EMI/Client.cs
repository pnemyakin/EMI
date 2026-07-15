using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace EMI
{
    using DebugLog;
    using MyException;
    using Network;
    using NGC;
    using RPCInternal;
    using System.Diagnostics;

    /// <summary>
    /// Клиент EMI
    /// </summary>
    public class Client : AClient
    {
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова [локальный - вызов будет произведён только у этого клиента]
        /// </summary>
        public readonly RPC LocalRPC = new();
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова [глобальный]
        /// </summary>
        public RPC RPC { get; private set; }
        /// <summary>
        /// Подключён ли клиент
        /// </summary>
        public bool IsConnect => MyNetworkClient.IsConnect;
        /// <summary>
        /// Этот клиент на стороне сервера?
        /// </summary>
        public bool IsServerSide => Server != null;

        private TimeSpan _PingPollingInterval = new(0, 0, 0, 15);
        /// <summary>
        /// Частота опроса пинга (устанавливается только на стороне клиента)
        /// </summary>
        public TimeSpan PingPollingInterval
        {
            get
            {
                if (IsServerSide)
                    return Server.PingPollingInterval;
                else
                    return _PingPollingInterval;
            }
            set
            {
                if (IsServerSide)
                    throw new Exception("Only on the client side!");
                else
                    _PingPollingInterval = value;
            }
        }
        /// <summary>
        /// Сколько байт в секунду отправляется
        /// </summary>
        public int SendByteSpeed => MyNetworkClient.SendByteSpeed;
        /// <summary>
        /// Число от 0 до 1 (сколько % байт доставленно) [если 0 то сеть сильно перегружена]
        /// </summary>
        public float DeliveredRate => MyNetworkClient.DeliveredRate;
        /// <summary>
        /// Какие пакеты можно игнорировать при перезгрузке сети
        /// </summary>
        public RandomDropType RandomDrop
        {
            get => MyNetworkClient.RandomDrop;
            set => MyNetworkClient.RandomDrop = value;
        }
        /// <summary>
        /// Ping
        /// </summary>
        public TimeSpan Ping = new TimeSpan(0);
        /// <summary>
        /// Время после которого будет произведено отключение
        /// </summary>
        public TimeSpan PingTimeout = new TimeSpan(0, 1, 0);
        /// <summary>
        /// Вызывается если произошло отключение
        /// </summary>
        public event INetworkClientDisconnected Disconnected;
        /// <summary>
        /// Максимальный размер пакета который может отправить удалённый пользователь за один раз (если размер будет превышен - клиент будет отключен)
        /// </summary>
        public int MaxPacketAcceptSize = 1024 * 1024 * 64; //64 мегабайт
        /// <summary>
        /// Ограничитель частоты RPC-вызовов (null = без ограничений)
        /// </summary>
        private RpcRateLimiter _rateLimiter;
        /// <summary>
        /// Интерфейс отправки/считывания датаграмм
        /// </summary>
        internal INetworkClient MyNetworkClient;
        /// <summary>
        /// Когда приходил прошлый запрос о пинге (для time out)
        /// </summary>
        private DateTime LastPing;
        /// <summary>
        /// Токен вызывающийся при отмене подключения (все операции должны быть отменены)
        /// </summary>
        internal CancellationTokenSource CancellationRun = new CancellationTokenSource();
        internal InputStackBuffer InputStack;
        private readonly static TaskFactory TaskLongFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);
        /// <summary>
        /// Ссылка на сервер (если клиент серверный)
        /// </summary>
        private readonly Server Server;
        /// <summary>
        /// Логи сервера и клиентов
        /// </summary>
        public readonly Logger Logger;
        /// <summary>
        ///  Возвращает адресс удалённого связанного клиента
        /// </summary>
        /// <summary>
        /// Последний известный адрес (сохраняется даже после отключения, чтобы логи были читаемы).
        /// </summary>
        private string _remoteAddressCache = "none";

        public string RemoteAddress
        {
            get
            {
                if (IsConnect)
                {
                    _remoteAddressCache = MyNetworkClient.GetRemoteClientAddress();
                    return _remoteAddressCache;
                }
                return _remoteAddressCache;
            }
        }

        /// <summary>
        /// Клиент, чей RPC обрабатывается прямо сейчас на этом потоке (thread-local).
        /// Устанавливается в RPCRunWithReturn/RPCRun перед вызовом обработчика.
        /// Используется серверными обработчиками для определения вызывающего клиента.
        /// </summary>
        [ThreadStatic]
        public static Client CurrentCaller;

        /// <summary>
        /// Middleware цепочка (null = отключено, нулевой overhead)
        /// </summary>
        private IPacketMiddleware[] _middlewares;

        /// <summary>
        /// Включить AES-256-GCM шифрование. Ключ согласуется при Connect() через <see cref="KeyExchange"/>
        /// (по умолчанию RSA key-transport) и НЕ передаётся открытым текстом. Устанавливать ДО Connect().
        /// </summary>
        public bool UseEncryption { get; set; }

        /// <summary>
        /// Включить LZ4 сжатие.
        /// Устанавливать ДО Connect().
        /// </summary>
        public bool UseCompression { get; set; }

        /// <summary>
        /// Стратегия согласования ключа шифрования (должна соответствовать ступени сервера).
        /// Если <see cref="UseEncryption"/>=true и здесь null — при Connect() создаётся
        /// <see cref="Network.RsaKeyExchange.CreateClient()"/> (уровень 2: доверяет ключу из handshake,
        /// защита от прослушки, НЕ от MITM). Для защиты от MITM — <see cref="Network.RsaKeyExchange.CreateClientPinned"/>
        /// с публичным ключом сервера; для PSK — <see cref="Network.PreSharedKeyExchange"/>.
        /// </summary>
        public IKeyExchange KeyExchange { get; set; }

        /// <summary>
        /// Стратегия исполнения входящих RPC-обработчиков (на каком потоке и в каком порядке).
        /// По умолчанию <see cref="ThreadPoolDispatcher"/> (пул потоков, параллельно — как раньше).
        /// Для Unity/движков задайте <see cref="PumpDispatcher"/> или <see cref="SynchronizationContextDispatcher"/>.
        /// На серверном клиенте наследуется от <see cref="Server.Dispatcher"/>. Никогда не null.
        /// </summary>
        public IRpcDispatcher Dispatcher
        {
            get => _dispatcher;
            set => _dispatcher = value ?? throw new ArgumentNullException(nameof(value));
        }
        private IRpcDispatcher _dispatcher = ThreadPoolDispatcher.Instance;

        /// <summary>
        /// Инициализирует клиента но не подключает к серверу
        /// </summary>
        /// <param name="network">интерфейс подключения</param>
        /// <exception cref="PlatformNotSupportedException">Платформа big-endian не поддерживается</exception>
        public Client(INetworkService network)
        {
            ThrowIfBigEndian();
            Logger = new Logger();
            MyNetworkClient = network.GetNewClient();
            RPC = LocalRPC;
            Init();

            Logger.Log(this, Messages.InitClientSide, network);
        }

        /// <summary>
        /// Устанавливает цепочку middleware (сжатие, шифрование) вручную.
        /// Вызывать ДО Connect(). Порядок: compress → encrypt.
        /// Передать null или пустой массив для отключения.
        /// Если используете UseEncryption/UseCompression — вызывать не нужно.
        /// </summary>
        /// <param name="middlewares">Цепочка middleware для обработки пакетов</param>
        public void UseMiddleware(params IPacketMiddleware[] middlewares)
        {
            if (IsConnect)
                throw new InvalidOperationException("Cannot change middleware while connected");
            _middlewares = middlewares != null && middlewares.Length > 0 ? middlewares : null;
        }

        /// <summary>
        /// Для сосздания клиента на стороне сервера
        /// </summary>
        /// <param name="network"></param>
        /// <param name="rpc"></param>
        /// <param name="server"></param>
        /// <param name="middlewares">Middleware цепочка от сервера (null = без middleware)</param>
        internal Client(INetworkClient network, RPC rpc, Server server, IPacketMiddleware[] middlewares = null)
        {
            Logger = server.Logger;
            _middlewares = middlewares;
            if (_middlewares != null && _middlewares.Length > 0)
                MyNetworkClient = new MiddlewareNetworkClient(network, _middlewares);
            else
                MyNetworkClient = network;
            RPC = rpc;
            Server = server;
            _dispatcher = server.Dispatcher; // наследуем стратегию исполнения RPC от сервера

            // Инициализация rate limiter из конфигурации сервера
            if (server.RateLimit != null)
                _rateLimiter = new RpcRateLimiter(server.RateLimit);

            Init();
            RunProcces();

            Logger.Log(this, Messages.InitServerSide, network);
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
        /// Для инициализации клиента
        /// </summary>
        private void Init()
        {
            InputStack = new InputStackBuffer(64, 134217728);
            RPCReturn = new ConcurrentDictionary<int, RCWaitHandle>();
            MyNetworkClient.Disconnected += LowDisconnect;
        }

        /// <summary>
        /// Подключиться к серверу
        /// </summary>
        /// <param name="address">адрес сервера</param>
        /// <param name="token">токен отмены задачи</param>
        /// <returns>было ли произведено подключение</returns>
        public async Task<bool> Connect(string address, CancellationToken token)
        {
            Logger.Log(this, Messages.ConnectBegin, IsConnect, IsServerSide);

            if (IsConnect)
            {
                Logger.Log(this, Messages.AlreadyRunning);
                throw new AlreadyException(Messages.AlreadyRunning.Message);
            }
            if (IsServerSide)
            {
                Logger.Log(this, Messages.PossibleReconnect);
                throw new Exception(Messages.PossibleReconnect.Message);
            }

            CancellationRun = new CancellationTokenSource();

            // Авто-путь: если шифрование включено, а стратегия не задана — RSA уровня 2
            // (доверяет ключу из handshake: защита от прослушки, не от MITM).
            if (_middlewares == null && UseEncryption && KeyExchange == null)
                KeyExchange = Network.RsaKeyExchange.CreateClient();

            var status = await MyNetworkClient.Connect(address, token).ConfigureAwait(false);


            Logger.Log(this, Messages.ConnectStatus, status);

            if (token.IsCancellationRequested && status == true)
            {
                Logger.Log(this, Messages.ConnectCanceled);
                MyNetworkClient.Disconnect(Messages.ConnectCanceled.Message);
                DoCancellationRun();

                Logger.Log(this, Messages.ConnectUnsuccessful);
                return false;
            }

            if (status == true)
            {
                // Ручные middleware — прямая обёртка без handshake (пользователь координирует ключ сам).
                if (_middlewares != null && _middlewares.Length > 0)
                {
                    MyNetworkClient = new MiddlewareNetworkClient(MyNetworkClient, _middlewares);
                }
                // Авто-путь: договариваемся о флагах + обмене ключами (v3, ключ не идёт открытым текстом).
                else if (UseCompression || (KeyExchange != null && KeyExchange.ProducesKey))
                {
                    var result = await MiddlewareNetworkClient.PerformClientHandshake(
                        MyNetworkClient, UseCompression, KeyExchange, token).ConfigureAwait(false);

                    if (result.Error != null)
                    {
                        Logger.Log(this, Messages.ConnectCanceled);
                        MyNetworkClient.Disconnect(result.Error);
                        DoCancellationRun();
                        throw new InvalidOperationException(result.Error);
                    }

                    if (result.Middlewares != null && result.Middlewares.Length > 0)
                    {
                        _middlewares = result.Middlewares;
                        MyNetworkClient = new MiddlewareNetworkClient(MyNetworkClient, _middlewares);
                    }
                }

                RunProcces();
                Logger.Log(this, Messages.ConnectDone);
                return true;
            }
            else
            {
                Logger.Log(this, Messages.ConnectUnsuccessful);
                return false;
            }
        }

        /// <summary>
        /// Закрывает соединение
        /// </summary>
        /// <param name="user_error">что сообщить клиенту при отключении</param>
        public void Disconnect(string user_error = "unknown")
        {
            Logger.Log(this, Messages.ClientDisconnect, user_error);

            if (!IsConnect)
            {
                Logger.Log(this, Messages.AlreadyDisconnected);
                throw new AlreadyException(Messages.AlreadyDisconnected.Message);
            }
            MyNetworkClient.Disconnect(user_error);
            //DoCancellationRun();
        }

        /// <summary>
        /// Вызвать при внутренем отключение
        /// </summary>
        /// <param name="error"></param>
        private void LowDisconnect(string error)
        {
            Logger.Log(this, Messages.LowDisconnectError, error);
            Disconnected?.Invoke(error);
            DoCancellationRun();
        }

        /// <summary>
        /// Отменить работу клиента
        /// </summary>
        private void DoCancellationRun()
        {
            try
            {
                CancellationRun.Cancel();
                CancellationRun.Dispose();
                CancellationRun = new CancellationTokenSource();
                //var cr = CancellationRun;
                //CancellationRun = null;
                //cr.Cancel();
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000).ConfigureAwait(false);
                        //cr.Dispose();
                    }
                    catch (Exception e)
                    {
                        Logger.Log(this, Messages.DoCancellationError, e.ToString());
                    }
                });
            }
            finally
            {
                //сброс компонентов для реиспользования клиента
                if (!IsServerSide)
                {
                    RPCReturn.Clear();
                }
            }
        }

        /// <summary>
        /// Запускает все необходимые потоки
        /// </summary>
        private void RunProcces()
        {
            TaskLongFactory.StartNew(() =>
            {
                _ = RunProcessAccept(CancellationRun.Token);
            });
            RunProccesPing(CancellationRun.Token);
        }

        /// <summary>
        /// Отвечает за отправку пинга + за отключение по ping timeout
        /// </summary>
        /// <param name="token">токен отмены</param>
        private void RunProccesPing(CancellationToken token)
        {
            TaskLongFactory.StartNew(async () =>
            {
                Logger.Log(this, Messages.PingStart);
                LastPing = TickTime.Now;
                const int size = DPack.sizeof_DPing + 1;
                var array = new EasyArray(size);
                array.Bytes[0] = (byte)PacketType.Ping_Send;

                void pingTask()
                {
                    try
                    {
                        if (TickTime.Now - LastPing > PingTimeout)
                        {
                            var ping = (TickTime.Now - LastPing).TotalMilliseconds;
                            Logger.Log(this, Messages.PingTimeout, ping);
                            MyNetworkClient.Disconnect(Messages.PingTimeout.Format(ping));

                            if (IsServerSide)
                                lock (Server)
                                    Server.PingSend -= pingTask;
                            return;
                        }
                        else
                        {
                            DPack.DPing.PackUP(array.Bytes, 1, TickTime.Now);
                            _ = MyNetworkClient.Send(array, true, token);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log(this, Messages.PingError, e.ToString());

                        MyNetworkClient.Disconnect(Messages.PingError.Format(e.ToString()));
                        if (IsServerSide)
                            lock (Server)
                                Server.PingSend -= pingTask;
                        return;
                    }
                }

                if (IsServerSide)
                {
                    lock (Server)
                        Server.PingSend += pingTask;
                }
                else
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(_PingPollingInterval).ConfigureAwait(false);
                        pingTask();
                    }
                    Logger.Log(this, Messages.PingStopped);
                }
            });
        }

        /// <summary>
        /// Основной процесс обработки входящих пакетов
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task RunProcessAccept(CancellationToken token)
        {
            Logger.Log(this, Messages.AcceptStart);

            async Task process()
            {
                try
                {
                    await ProccesAccept(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Log(this, Messages.AcceptPacketError, e.ToString());
                    MyNetworkClient.Disconnect(Messages.AcceptPacketError.Format(e.ToString()));
                }
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var packet = await MyNetworkClient.AcceptPacket(MaxPacketAcceptSize, token);
                    if (packet.IsEmpty())
                        break;

                    await InputStack.Push(packet, token).ConfigureAwait(false);
                    _ = Task.Run(() => process());
                }
                Logger.Log(this, Messages.AcceptStopped);
            }
            catch (Exception e)
            {
                Logger.Log(this, Messages.AcceptPacketError, e.ToString());
            }
        }

        /// <summary>
        /// Процесс обработки пакета
        /// </summary>
        /// <param name="token">токен отмены</param>
        /// <returns></returns>
        private async Task ProccesAccept(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            var ArrayHandle = await InputStack.Pop(token);
            // Владение хендлом может быть передано диспетчеру (для RPC-веток): тогда его освободит
            // замыкание после funcs.Invoke, а не этот finally. Struct-хендл диспозится ровно один раз.
            bool handleTransferred = false;
            try
            {
                if (token.IsCancellationRequested || ArrayHandle.Buffer.IsEmpty())
                    return;
                var array = ArrayHandle.Buffer;
                PacketType packetType = (PacketType)array.Bytes[array.Offset];
                //Debug.WriteLine("accept => " + packetType);
                array.Offset += 1;
                // Rate limiting (только для RPC-пакетов, пинг не ограничивается)
                if (_rateLimiter != null && IsRpcPacket(packetType))
                {
                    var rlResult = _rateLimiter.TryConsume();
                    if (rlResult == RateLimitResult.Kick)
                    {
                        Logger.Log(this, Messages.RateLimitKick, _rateLimiter.SoftDropCount);
                        MyNetworkClient.Disconnect(Messages.RateLimitKick.Format(_rateLimiter.SoftDropCount));
                        return;
                    }
                    if (rlResult == RateLimitResult.SoftDrop)
                    {
                        Logger.Log(this, Messages.RateLimitSoftDrop, _rateLimiter.SoftDropCount);
                        return;
                    }
                }

                // Любой входящий пакет подтверждает что соединение живо
                LastPing = TickTime.Now;

                switch (packetType)
                {
                    case PacketType.Ping_Send:
                        array.Bytes[array.Offset - 1] = (byte)PacketType.Ping_Receive;
                        await MyNetworkClient.Send(array, true, token).ConfigureAwait(false);
                        break;
                    case PacketType.Ping_Receive:
                        DPack.DPing.UnPack(array.Bytes, array.Offset, out var time);
                        Ping = TickTime.Now - time;
                        break;
                    case PacketType.RPC_Simple:
                        {
                            // Владение хендлом уходит в диспетчер: Invoke исполнится на выбранном
                            // потоке, хендл освободится после него. Здесь finally его не трогает.
                            DispatchRPCRun(false, ArrayHandle, token);
                            handleTransferred = true;
                        }
                        break;
                    case PacketType.RPC_Return:
                        {
                            DispatchRPCRun(true, ArrayHandle, token);
                            handleTransferred = true;
                        }
                        break;
                    case PacketType.RPC_Returned:
                        {
                            // Распаковываем callId (не methodId)
                            DPack.DRPCReturned.UnPack(array.Bytes, array.Offset, out var callId);
                            array.Offset += DPack.sizeof_DRPCReturned;

                            RCWaitHandle handle;

                            // Short window to handle race where RPC_Returned arrives before TryAdd.
                            // On timeout: silently drop the stale packet — do NOT disconnect.
                            var source = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            while (!RPCReturn.TryGetValue(callId, out handle))
                            {
                                if (token.IsCancellationRequested)
                                {
                                    source.Dispose();
                                    return; // connection closing — nothing to do
                                }
                                if (source.IsCancellationRequested)
                                {
                                    Logger.Log(this, Messages.RPCReturnNotFound); // warn only, do NOT disconnect
                                    source.Dispose();
                                    return; // stale/unknown callId — drop packet, keep connection alive
                                }
                                await Task.Delay(1).ConfigureAwait(false);
                            }

                            source.Dispose();

                            RPCReturn.TryRemove(callId, out _);
                            // Копируем данные ответа, так как array будет возвращён в пул
                            int dataLength = array.Length - array.Offset;
                            byte[] resultBytes = new byte[dataLength];
                            System.Array.Copy(array.Bytes, array.Offset, resultBytes, 0, dataLength);
                            handle.ResultData = new EasyArray(resultBytes);
                            handle.Semaphore.Release();
                        }
                        break;
                    case PacketType.RPC_Forwarding:
                        if (!IsServerSide)
                        {
                            Logger.Log(this, Messages.BadPacketTypeForwarding);
                            MyNetworkClient.Disconnect(Messages.BadPacketTypeForwarding.Message);
                        }
                        else
                        {
                            //*при RPC_Forwarding отправляется сообщение на сервер содержащие флаг - гарантированное ли было сообщение, а после она рассылается как обычное сообщение*//
                            DPack.DForwarding.UnPack(array.Bytes, array.Offset, out var guarant, out var id);
                            array.Offset++;
                            var forwardingInfo = RPC.TryGetRegisteredForwarding(id);

                            if (forwardingInfo == null)
                            {
                                forwardingInfo = LocalRPC.TryGetRegisteredForwarding(id);
                            }

                            if (forwardingInfo != null)
                            {
                                var clients = forwardingInfo(this);
                                using (var arraySend = new NGCArray(array.Length - 1))
                                {
                                    arraySend.Bytes[0] = (byte)PacketType.RPC_Simple;
                                    Buffer.BlockCopy(array.Bytes, array.Offset, arraySend.Bytes, 1, arraySend.Length - 1);

                                    for (int i = 0; i < clients.Length; i++)
                                    {
                                        if (clients[i] != null && clients[i].IsConnect)
                                        {
                                            await clients[i].MyNetworkClient.Send(arraySend, guarant, token).ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Logger.Log(this, Messages.NotFoundForwarding);
                            }
                        }
                        break;
                    default:
                        Logger.Log(this, Messages.BadPacketType);
                        MyNetworkClient.Disconnect(Messages.BadPacketType.Message);
                        break;
                }
            }
            finally
            {
                if (!handleTransferred)
                    ArrayHandle.Dispose();
            }
        }

        /// <summary>
        /// Разбирает заголовок RPC-пакета на сетевом потоке, ищет метод, и передаёт САМ ВЫЗОВ
        /// обработчика в <see cref="Dispatcher"/> (там он исполнится на нужном потоке).
        /// Владелец <paramref name="handle"/> — этот метод: буфер освобождается после Invoke.
        /// Отправка ответа (RPC_Returned) идёт fire-and-forget на сетевом потоке, чтобы не
        /// блокировать поток диспетчера (например, main-поток Unity) на сетевом I/O.
        /// </summary>
        /// <param name="needReturn">true для RPC_Return (нужно отправить результат)</param>
        /// <param name="handle">хендл входного буфера; владение переходит сюда</param>
        /// <param name="token">токен отмены</param>
        private void DispatchRPCRun(bool needReturn, InputStackBuffer.Handle handle, CancellationToken token)
        {
            var array = handle.Buffer;
            long methodId;
            int callId = 0;
            if (needReturn)
            {
                DPack.DRPCReturn.UnPack(array.Bytes, array.Offset, out methodId, out callId);
                array.Offset += DPack.sizeof_DRPCReturn;
            }
            else
            {
                DPack.DRPC.UnPack(array.Bytes, array.Offset, out methodId);
                array.Offset += sizeof(long);
            }

            var funcs = RPC.TryGetRegisteredMethod(methodId) ?? LocalRPC.TryGetRegisteredMethod(methodId);
            if (funcs == null)
            {
                // Незарегистрированный метод — дальше диспетчер не гоняем, буфер освобождаем сразу.
                handle.Dispose();
                string methodInfo = RPC.GetMethodInfo(methodId);
                Logger.Log(this, Messages.RPCNotFound, methodId, methodInfo);
#if DEBUG
                Logger.Log(this, Messages.RPCRegisteredList, RPC.GetRegisteredMethodsList());
                if (RPC != LocalRPC)
                    Logger.Log(this, Messages.RPCRegisteredListLocal, LocalRPC.GetRegisteredMethodsList());
#endif
                return;
            }

            int capturedCallId = callId;
            _dispatcher.Post(() =>
            {
                INGCArray sendArray = null;
                try
                {
                    CurrentCaller = this; // серверные хендлеры видят вызвавшего клиента
                    var @return = funcs.Invoke(array);

                    // Ответ пакуем ЗДЕСЬ, пока входной буфер ещё жив (возврат может алиасить его,
                    // напр. RawBuffer). Отправку выносим на сетевой поток — main-поток не ждёт I/O.
                    if (needReturn)
                    {
                        const int bsize = DPack.sizeof_DRPCReturned + 1;
                        int size = bsize + (@return?.PackSize ?? 0);
                        sendArray = new NGCArray(size);
                        sendArray.Bytes[0] = (byte)PacketType.RPC_Returned;
                        DPack.DRPCReturned.PackUP(sendArray.Bytes, 1, capturedCallId);
                        if (@return != null)
                        {
                            sendArray.Offset += bsize;
                            @return.PackUp(sendArray);
                            sendArray.Offset = 0; // курсор записи → начало данных
                        }
                    }
                }
                catch (Exception e)
                {
                    // Ошибка распаковки/обработки не должна ронять приёмный цикл или глушиться пулом.
                    Logger.Log(this, Messages.AcceptPacketError, e.ToString());
                    sendArray?.Dispose();
                    sendArray = null;
                }
                finally
                {
                    handle.Dispose(); // входной буфер после Invoke + упаковки больше не нужен
                }

                if (sendArray != null)
                    _ = SendReturn(sendArray, token);
            });
        }

        /// <summary>
        /// Отправляет уже упакованный ответ RPC_Returned. Вынесено из потока диспетчера,
        /// чтобы пользовательский поток (main-поток движка) не ждал сетевой I/O.
        /// </summary>
        private async Task SendReturn(INGCArray sendArray, CancellationToken token)
        {
            try
            {
                await MyNetworkClient.Send(sendArray, true, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Log(this, Messages.AcceptPacketError, e.ToString());
            }
            finally
            {
                sendArray.Dispose();
            }
        }

        /// <summary>
        /// Проверяет, является ли тип пакета RPC-вызовом (подлежит rate limiting).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRpcPacket(PacketType type)
        {
            return type == PacketType.RPC_Simple
                || type == PacketType.RPC_Return
                || type == PacketType.RPC_Forwarding;
        }

        /// <summary>
        /// Обёртка отправки пакета
        /// </summary>
        /// <param name="array"></param>
        /// <param name="guaranteed"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override async Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            await MyNetworkClient.Send(array, guaranteed, token).ConfigureAwait(false);
        }
    }
}