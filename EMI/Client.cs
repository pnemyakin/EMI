using System;
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

    /// <summary>
    /// Клиент EMI
    /// </summary>
    public class Client : AClient
    {
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова [локальный - вызов будет произведён только у этого клиента]
        /// </summary>
        public readonly RPC LocalRPC = new RPC();
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

        private TimeSpan _PingPollingInterval = new TimeSpan(0, 0, 0, 15);
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
        public int MaxPacketAcceptSize = 1024 * 1024 * 10; //10 мегобайт
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
        public string RemoteAddress
        {
            get
            {
                if (IsConnect)
                {
                    return MyNetworkClient.GetRemoteClientAddress();
                }
                else
                {
                    return "none";
                }
            }
        }

        /// <summary>
        /// Инициализирует клиента но не подключает к серверу
        /// </summary>
        /// <param name="network">интерфейс подключения</param>
        public Client(INetworkService network)
        {
            Logger = new Logger();
            MyNetworkClient = network.GetNewClient();
            RPC = LocalRPC;
            Init();

            Logger.Log(this, Messages.InitClientSide, network);
        }

        /// <summary>
        /// Для сосздания клиента на стороне сервера
        /// </summary>
        /// <param name="network"></param>
        /// <param name="rpc"></param>
        /// <param name="server"></param>
        internal Client(INetworkClient network, RPC rpc, Server server)
        {
            Logger = server.Logger;
            MyNetworkClient = network;
            RPC = rpc;
            Server = server;
            Init();
            RunProcces();

            Logger.Log(this, Messages.InitServerSide, network);
        }
        /// <summary>
        /// Для инициализации клиента
        /// </summary>
        private void Init()
        {
            InputStack = new InputStackBuffer(64, 134217728);
            RPCReturn = new Dictionary<int, RCWaitHandle>();
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
            Logger.Log(this, Messages.ConnectBeding, IsConnect, IsServerSide);

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

            var status = await MyNetworkClient.Сonnect(address, token).ConfigureAwait(false);


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
                        Logger.Log(this, Messages.DoCanellationError, e.ToString());
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
                    Logger.Log(this, Messages.PingStoped);
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

            void process()
            {
                try
                {
                    ProccesAccept(token);
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
                    _ = Task.Factory.StartNew(process);
                }
                Logger.Log(this, Messages.AcceptStoped);
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
        private async void ProccesAccept(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            using (var ArrayHandle = await InputStack.Pop(token))
            {
                if (token.IsCancellationRequested || ArrayHandle.Buffer.IsEmpty())
                    return;
                var array = ArrayHandle.Buffer;
                PacketType packetType = (PacketType)array.Bytes[array.Offset];
                //Debug.WriteLine("accept => " + packetType);
                array.Offset += 1;
                switch (packetType)
                {
                    case PacketType.Ping_Send:
                        array.Bytes[array.Offset - 1] = (byte)PacketType.Ping_Receive;
                        await MyNetworkClient.Send(array, true, token).ConfigureAwait(false);
                        break;
                    case PacketType.Ping_Receive:
                        DPack.DPing.UnPack(array.Bytes, array.Offset, out var time);
                        if (LastPing < time)
                            Ping = TickTime.Now - time;
                        LastPing = TickTime.Now;
                        break;
                    case PacketType.RPC_Simple:
                        {
                            _ = RPCRun(false, array, token).ConfigureAwait(false);
                        }
                        break;
                    case PacketType.RPC_Return:
                        {
                            _ = RPCRun(true, array, token).ConfigureAwait(false);
                        }
                        break;
                    case PacketType.RPC_Returned:
                        {
                            DPack.DRPC.UnPack(array.Bytes, array.Offset, out var id);
                            array.Offset += sizeof(int);

                            RCWaitHandle handle;

                            CancellationTokenSource source = new CancellationTokenSource(new TimeSpan(0, 5, 0));

                            while (!RPCReturn.TryGetValue(id, out handle))
                            {
                                if (source.IsCancellationRequested)
                                {
                                    Logger.Log(this, Messages.RPCReturnNotFound);
                                    MyNetworkClient.Disconnect(Messages.RPCReturnNotFound.Message);
                                    return;
                                }
                                await Task.Yield();
                            }

                            source.Dispose();

                            lock (RPCReturn)
                            {
                                RPCReturn.Remove(id);
                            }
                            handle.Indicator.UnPack(array);
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
        }

        /// <summary>
        /// Вызов метода
        /// </summary>
        /// <param name="needReturn">нужно ли отправить результат</param>
        /// <param name="array">массив данных для отправки</param>
        /// <param name="token">токен отмены</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task RPCRun(bool needReturn, INGCArray array, CancellationToken token)
        {
            DPack.DRPC.UnPack(array.Bytes, array.Offset, out var id);
            array.Offset += sizeof(int);
            var funcs = RPC.TryGetRegisteredMethod(id);

            if (funcs == null)
            {
                funcs = LocalRPC.TryGetRegisteredMethod(id);
            }

            if (funcs != null)
            {
                if (needReturn)
                {
                    var @return = funcs.Invoke(array);
                    const int bsize = DPack.sizeof_DRPC + 1;
                    int size = bsize;
                    if (@return != null)
                        size += @return.PackSize;

                    INGCArray sendArray = new NGCArray(size);
                    try
                    {
                        sendArray.Bytes[0] = (byte)PacketType.RPC_Returned;
                        DPack.DRPC.PackUP(sendArray.Bytes, 1, id);
                        if (@return != null)
                        {
                            sendArray.Offset += bsize;
                            @return.PackUp(sendArray);
                        }
                        await MyNetworkClient.Send(sendArray, true, token);
                    }
                    finally
                    {
                        sendArray.Dispose();
                    }
                }
                else
                {
                    funcs.Invoke(array);
                }
            }
            else
            {
                Logger.Log(this, Messages.RPCNotFount, id);
            }
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