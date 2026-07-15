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
        /// Middleware для обработки пакетов (null = без middleware)
        /// </summary>
        private IPacketMiddleware[] _middlewares;

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
            RPCReturn = new System.Collections.Concurrent.ConcurrentDictionary<int, RCWaitHandle>();
        }

        /// <summary>
        /// Устанавливает middleware для обработки исходящих/входящих пакетов.
        /// Порядок: compress → encrypt (при отправке), decrypt → decompress (при приёме).
        /// </summary>
        public void UseMiddleware(params IPacketMiddleware[] middlewares)
        {
            _middlewares = middlewares != null && middlewares.Length > 0 ? middlewares : null;
        }

        /// <summary>
        /// Заставляет клиента проверить пинг один раз (thread-safe)
        /// </summary>
        public void OneSendPing()
        {
            const int size = DPack.sizeof_DPing + 1;
            var array = new EasyArray(size);
            array.Bytes[0] = (byte)PacketType.Ping_Send;
            DPack.DPing.PackUP(array.Bytes, 1, TickTime.Now);
            SendWithMiddleware(array, true);
        }

        /// <summary>
        /// Процесс обработки пакета.
        /// Если установлен middleware, входящий пакет будет обработан (расшифрован/распакован) перед разбором.
        /// </summary>
        public void AcceptPacket(INGCArray array)
        {
            // Применяем middleware (reverse chain: decrypt → decompress)
            if (_middlewares != null)
            {
                INGCArray current = array;
                try
                {
                    for (int i = _middlewares.Length - 1; i >= 0; i--)
                    {
                        var next = _middlewares[i].ProcessIncoming(current);
                        if (current != array)
                            current.Dispose();
                        current = next;
                    }
                    AcceptPacketInner(current);
                }
                finally
                {
                    if (current != array)
                        current?.Dispose();
                }
                return;
            }
            AcceptPacketInner(array);
        }

        private void AcceptPacketInner(INGCArray array)
        {
            PacketType packetType = (PacketType)array.Bytes[array.Offset];
            array.Offset += 1;
            switch (packetType)
            {
                case PacketType.Ping_Send:
                    array.Bytes[array.Offset - 1] = (byte)PacketType.Ping_Receive;
                    SendWithMiddleware(array, true);
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
                        RPCRunWithReturn(array);
                    }
                    break;
                case PacketType.RPC_Returned:
                    {
                        // Распаковываем callId (не methodId)
                        DPack.DRPCReturned.UnPack(array.Bytes, array.Offset, out var callId);
                        array.Offset += DPack.sizeof_DRPCReturned;

                        if (RPCReturn.TryRemove(callId, out var handle))
                        {
                            // Копируем данные ответа, так как array будет возвращён в пул
                            int dataLength = array.Length - array.Offset;
                            byte[] resultBytes = new byte[dataLength];
                            System.Array.Copy(array.Bytes, array.Offset, resultBytes, 0, dataLength);
                            handle.ResultData = new EasyArray(resultBytes);
                            handle.Semaphore.Release();
                        }
                        else
                        {
                            // Handle может ещё не быть зарегистрирован — ждём асинхронно
                            Task.Run(async () =>
                            {
                                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                                {
                                    while (!RPCReturn.TryRemove(callId, out handle))
                                    {
                                        if (cts.IsCancellationRequested)
                                            return;
                                        await Task.Delay(1).ConfigureAwait(false);
                                    }
                                    // Копируем данные ответа, так как array будет возвращён в пул
                                    int dataLen = array.Length - array.Offset;
                                    byte[] resBytes = new byte[dataLen];
                                    System.Array.Copy(array.Bytes, array.Offset, resBytes, 0, dataLen);
                                    handle.ResultData = new EasyArray(resBytes);
                                    handle.Semaphore.Release();
                                }
                            });
                        }
                    }
                    break;
                case PacketType.RPC_Forwarding:
                    throw new NotSupportedException("PacketType.RPC_Forwarding");
                default:
                    throw new NotSupportedException(Messages.BadPacketType.Message);
            }
        }

        /// <summary>
        /// Вызов метода (без возврата значения)
        /// </summary>
        /// <param name="needReturn">нужно ли отправить результат</param>
        /// <param name="array">массив данных для отправки</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RPCRun(bool needReturn, INGCArray array)
        {
            DPack.DRPC.UnPack(array.Bytes, array.Offset, out var id);
            array.Offset += sizeof(long);
            var funcs = RPC.TryGetRegisteredMethod(id);

            if (funcs == null)
            {
                funcs = LocalRPC.TryGetRegisteredMethod(id);
            }

            if (funcs != null)
            {
                if (needReturn)
                {
                    // Headless синхронен (исполнение на потоке вызывающего AcceptPacket).
                    // ValueTask sync-хендлера завершён синхронно; для async-хендлера здесь
                    // произойдёт кооперативное ожидание завершения.
                    var @return = funcs.Invoke(array).GetAwaiter().GetResult();
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
                        sendArray.Offset = 0; // сбрасываем offset перед отправкой
                        SendWithMiddleware(sendArray, true);
                    }
                    finally
                    {
                        sendArray.Dispose();
                    }
                }
                else
                {
                    funcs.Invoke(array).GetAwaiter().GetResult();
                }
            }
            else
            {
#if DEBUG
                Console.WriteLine($"EMI => Warning => HeadlessHandler: {Messages.RPCNotFound.Format(id, "unknown")}");
#endif
            }
        }

        /// <summary>
        /// Вызов метода с возвратом значения (RPC_Return).
        /// Извлекает methodId и callId из пакета, отправляет callId в ответе.
        /// </summary>
        /// <param name="array">массив данных</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RPCRunWithReturn(INGCArray array)
        {
            DPack.DRPCReturn.UnPack(array.Bytes, array.Offset, out var methodId, out var callId);
            array.Offset += DPack.sizeof_DRPCReturn;
            var funcs = RPC.TryGetRegisteredMethod(methodId);

            if (funcs == null)
            {
                funcs = LocalRPC.TryGetRegisteredMethod(methodId);
            }

            if (funcs != null)
            {
                var @return = funcs.Invoke(array).GetAwaiter().GetResult();
                const int bsize = DPack.sizeof_DRPCReturned + 1;
                int size = bsize;
                if (@return != null)
                    size += @return.PackSize;

                INGCArray sendArray = new NGCArray(size);
                try
                {
                    sendArray.Bytes[0] = (byte)PacketType.RPC_Returned;
                    DPack.DRPCReturned.PackUP(sendArray.Bytes, 1, callId);
                    if (@return != null)
                    {
                        sendArray.Offset += bsize;
                        @return.PackUp(sendArray);
                    }
                    sendArray.Offset = 0; // сбрасываем offset перед отправкой
                    SendWithMiddleware(sendArray, true);
                }
                finally
                {
                    sendArray.Dispose();
                }
            }
            else
            {
#if DEBUG
                Console.WriteLine($"EMI => Warning => HeadlessHandler: {Messages.RPCNotFound.Format(methodId, "unknown")}");
#endif
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
            SendWithMiddleware(array, guaranteed);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Применяет middleware (compress → encrypt) и передаёт в OnSendPacket
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendWithMiddleware(INGCArray array, bool guaranteed)
        {
            if (_middlewares == null || _middlewares.Length == 0)
            {
                OnSendPacket(array, guaranteed);
                return;
            }

            INGCArray current = array;
            INGCArray previous = null;
            try
            {
                for (int i = 0; i < _middlewares.Length; i++)
                {
                    var next = _middlewares[i].ProcessOutgoing(current);
                    if (previous != null)
                        previous.Dispose();
                    previous = current != array ? current : null;
                    current = next;
                }
                if (previous != null && previous != current)
                    previous.Dispose();

                OnSendPacket(current, guaranteed);
            }
            finally
            {
                if (current != null && current != array)
                    current.Dispose();
            }
        }
    }
}
