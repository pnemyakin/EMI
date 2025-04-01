using EMI.DebugLog;
using EMI.MyException;
using EMI.Network;
using EMI.NGC;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.Headless
{
    //[History был создан для steam P2P в 27.6.2024]

    /// <summary>
    /// Безсерверный RPC клиент, более гибкий в использовании, так же не имеет встроенной системы приёма-передачи сообщений
    /// (нет защиты на случай исключений от SendPacket)
    /// </summary>
    public class HeadlessHandler : AClient, IDisposable
    {
        /// <summary>
        /// функция отправки пакета аддресату
        /// </summary>
        /// <param name="array">данные, НЕ ВЫЗЫВАТЬ array.Dispose()!</param>
        /// <param name="guaranteed">гарантированная передача</param>
        public delegate void SendPacket(INGCArray array, bool guaranteed);
        /// <summary>
        /// Вызывается всякий раз когда клиенту нужно отправить пакет
        /// </summary>
        public SendPacket OnSendPacket { get; private set; }
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова [локальный - вызов будет произведён только у этого клиента]
        /// </summary>
        public readonly RPC LocalRPC;
        /// <summary>
        /// Отвечает за регистрирование удалённых процедур для последующего вызова [глобальный]
        /// </summary>
        public readonly RPC RPC;
        /// <summary>
        /// Ping
        /// </summary>
        public TimeSpan Ping = new TimeSpan(0);
        /// <summary>
        /// Когда приходил прошлый запрос о пинге (для time out)
        /// </summary>
        public DateTime LastPing;
        private readonly static TaskFactory TaskLongFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);

        /// <summary>
        /// Инициализирует HeadlessHandler
        /// </summary>
        /// <param name="onSendPacket">Метод отправки пакетов</param>
        /// <param name="rpc">если имеется несколько HeadlessHandler, можно указать для них глобальный (общий) RPC</param>
        public HeadlessHandler(SendPacket onSendPacket, RPC rpc = null)
        {
            if (rpc == null)
            {
                LocalRPC = RPC = new RPC();
            }
            else
            {
                RPC = rpc;
                LocalRPC = new RPC();
            }
            OnSendPacket = onSendPacket;
        }

        private readonly EasyArray PingPacketArray = InitPingPacketArray();

        private static EasyArray InitPingPacketArray()
        {
            const int size = DPack.sizeof_DPing + 1; 
            var array = new EasyArray(size);
            array.Bytes[0] = (byte)PacketType.Ping_Send;
            return array;
        }

        /// <summary>
        /// Заставляет клиента проверить пинг один раз
        /// </summary>
        public void OneSendPing()
        {
            DPack.DPing.PackUP(PingPacketArray.Bytes, 1, TickTime.Now);
            OnSendPacket(PingPacketArray, true);
        }

        /// <summary>
        /// Процесс обработки пакета
        /// </summary>
        public void AcceptPacket(INGCArray array)
        {
            PacketType packetType = (PacketType)array.Bytes[array.Offset];
            array.Offset += 1;
            switch (packetType)
            {
                case PacketType.Ping_Send:
                    array.Bytes[array.Offset - 1] = (byte)PacketType.Ping_Receive;
                    OnSendPacket(array, true);
                    break;
                case PacketType.Ping_Receive:
                    DPack.DPing.UnPack(array.Bytes, array.Offset, out var time);
                    if (LastPing < time)
                        Ping = TickTime.Now - time;
                    LastPing = TickTime.Now;
                    break;
                case PacketType.RPC_Simple:
                    {
                        RPCRun(false, array);
                    }
                    break;
                case PacketType.RPC_Return:
                    {
                        RPCRun(true, array);
                    }
                    break;
                case PacketType.RPC_Returned:
                    {
                        Task.Factory.StartNew(async () =>
                        {
                            DPack.DRPC.UnPack(array.Bytes, array.Offset, out var id);
                            array.Offset += sizeof(int);

                            RCWaitHandle handle;

                            CancellationTokenSource source = new CancellationTokenSource(new TimeSpan(0, 5, 0));

                            while (!RPCReturn.TryGetValue(id, out handle))
                            {
                                if (source.IsCancellationRequested)
                                {
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
                        });
                    }
                    break;
                case PacketType.RPC_Forwarding:
                    throw new NotSupportedException("PacketType.RPC_Forwarding");
                default:
                    throw new NotSupportedException(Messages.BadPacketType.Message);
            }
        }

        /// <summary>
        /// Вызов метода
        /// </summary>
        /// <param name="needReturn">нужно ли отправить результат</param>
        /// <param name="array">массив данных для отправки</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RPCRun(bool needReturn, INGCArray array)
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
                        OnSendPacket(sendArray, true);
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
                Console.WriteLine(Messages.RPCNotFount.Message, id);
            }
        }

        /// <summary>
        /// Просто убирает ссылки на события
        /// </summary>
        public void Dispose()
        {
            OnSendPacket = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            OnSendPacket(array, guaranteed);
            return Task.CompletedTask;
        }
    }
}
