using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.Indicators
{
    using NGC;
    /// <summary>
    /// Ссылка на удалённый метод
    /// </summary>
    public abstract class AIndicator
    {
        /// <summary>
        /// Глобальный счётчик для генерации уникальных call ID.
        /// </summary>
        private static int _callIdCounter = 0;

        /// <summary>
        /// айди вызываймой функции (64-битный FNV-1a хеш)
        /// </summary>
        protected internal long ID;
#if DEBUG
        /// <summary>
        /// Имя индикатора для отладки [только для DEBUG]
        /// </summary>
        internal string Name;
#endif
        /// <summary>
        /// Размер необходимый для упаковки параметров (устарел, используется для совместимости)
        /// </summary>
        protected internal abstract int Size { get; }
        /// <summary>
        /// Упаковщик (устарел, используется для совместимости)
        /// </summary>
        /// <param name="array"></param>
        protected internal abstract void PackUp(INGCArray array);
        /// <summary>
        /// Распаковщик (устарел, используется только для совместимости).
        /// Для ReturnWait результат возвращается через RCallLowPacked.
        /// </summary>
        /// <param name="array"></param>
        protected internal abstract void UnPack(INGCArray array);

        /// <summary>
        /// Вызов удалённого метода с предварительно упакованными данными.
        /// Потокобезопасный — данные передаются как параметры, не используются экземплярные поля.
        /// </summary>
        /// <param name="client">у кого вызвать</param>
        /// <param name="type">тип вызова</param>
        /// <param name="token">токен отмены</param>
        /// <param name="dataSize">размер данных параметров</param>
        /// <param name="packAction">действие для упаковки данных в массив (offset уже установлен)</param>
        /// <returns>Для ReturnWait — массив с данными ответа (вызывающая сторона распаковывает), иначе null</returns>
        internal async Task<INGCArray> RCallLowPacked(AClient client, RCType type, CancellationToken token, int dataSize, Action<INGCArray> packAction)
        {
            bool guarant = type != RCType.Fast && type != RCType.FastForwarding;
            byte packetType;
            int callId = 0;
            if (type == RCType.ReturnWait) {
                packetType = (byte)PacketType.RPC_Return;
                callId = Interlocked.Increment(ref _callIdCounter);
            }
            else if (type == RCType.FastForwarding || type == RCType.Forwarding) {
                packetType = (byte)PacketType.RPC_Forwarding;
            }
            else {
                packetType = (byte)PacketType.RPC_Simple;
            }

            INGCArray sendArray = default;
            try
            {
                if (packetType == (byte)PacketType.RPC_Forwarding) //PACK HEADER, init array
                {
                    const int bsize = DPack.sizeof_DForwarding + 1;
                    sendArray = new NGCArray(bsize + dataSize);
                    DPack.DForwarding.PackUP(sendArray.Bytes, 1, guarant, ID);
                    sendArray.Offset += bsize;
                }
                else if (packetType == (byte)PacketType.RPC_Return)
                {
                    // RPC_Return: method ID + call ID
                    const int bsize = DPack.sizeof_DRPCReturn + 1;
                    sendArray = new NGCArray(bsize + dataSize);
                    DPack.DRPCReturn.PackUP(sendArray.Bytes, 1, ID, callId);
                    sendArray.Offset += bsize;
                }
                else
                {
                    const int bsize = DPack.sizeof_DRPC + 1;
                    sendArray = new NGCArray(bsize + dataSize);
                    DPack.DRPC.PackUP(sendArray.Bytes, 1, ID);
                    sendArray.Offset += bsize;
                }
                sendArray.Bytes[0] = packetType;

                packAction?.Invoke(sendArray);
                sendArray.Offset = 0; // Offset использовался как курсор записи; данные начинаются с 0

                // Register the return handle BEFORE sending so that
                // RPC_Returned arriving on the receive thread always
                // finds the callId in RPCReturn (eliminates the race
                // that caused "TryGetValue => not found, timeout").
                RCWaitHandle handle = null;
                CancellationToken tokenPro = token;
                if (type == RCType.ReturnWait)
                {
                    handle = new RCWaitHandle(this, callId);
                    //TODO client.CancellationRun не реализован в AClient и HeadlessHandler
                    if (client is Client real_client)
                    {
                        tokenPro = CancellationTokenSource.CreateLinkedTokenSource(token, real_client.CancellationRun.Token).Token;
                    }

                    client.RPCReturn.TryAdd(callId, handle);
                }

                try
                {
                    await client.Send(sendArray, guarant, token).ConfigureAwait(false);
                }
                catch
                {
                    // Send failed — remove the pre-registered handle so it
                    // doesn't leak in RPCReturn.
                    if (handle != null)
                        client.RPCReturn.TryRemove(callId, out _);
                    throw;
                }

                if (handle != null)
                {
                    await handle.Semaphore.WaitAsync(tokenPro).ConfigureAwait(false);
                    return handle.ResultData;
                }
                return null;
            }
            finally
            {
                sendArray.Dispose();
            }
        }

        /// <summary>
        /// Вызов удалённого метода (устаревший метод, использует экземплярные поля).
        /// </summary>
        [Obsolete("Используйте RCallLowPacked для потокобезопасных вызовов")]
        internal async Task<INGCArray> RCallLow(AClient client, RCType type, CancellationToken token)
        {
            return await RCallLowPacked(client, type, token, Size, PackUp).ConfigureAwait(false);
        }
    }
}