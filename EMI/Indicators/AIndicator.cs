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
        /// айди вызываймой функции
        /// </summary>
        protected internal int ID;
#if DEBUG
        /// <summary>
        /// Имя индикатора для отладки [только для DEBUG]
        /// </summary>
        internal string Name;
#endif
        /// <summary>
        /// Размер необходимый для упаковки параметров
        /// </summary>
        protected internal abstract int Size { get; }
        /// <summary>
        /// Упаковщик
        /// </summary>
        /// <param name="array"></param>
        protected internal abstract void PackUp(INGCArray array);
        /// <summary>
        /// Распаковщик
        /// </summary>
        /// <param name="array"></param>
        protected internal abstract void UnPack(INGCArray array);

        /// <summary>
        /// Вызов удалённого метода
        /// </summary>
        /// <param name="client">у кого вызвать</param>
        /// <param name="type">тип вызова</param>
        /// <param name="token">токен отмены</param>
        /// <returns></returns>
        internal async Task RCallLow(AClient client, RCType type, CancellationToken token)
        {
            bool guarant = type != RCType.Fast && type != RCType.FastForwarding;
            byte packetType;
            if (type == RCType.ReturnWait) {
                packetType = (byte)PacketType.RPC_Return;
            }
            else if (type == RCType.FastForwarding || type == RCType.Forwarding) {
                packetType = (byte)PacketType.RPC_Forwarding;
            }
            else {
                packetType = (byte)PacketType.RPC_Simple;
            }

            INGCArray sendArray=default;
            try
            {
                if (packetType == (byte)PacketType.RPC_Forwarding) //PACK HEADER, init array
                {
                    const int bsize = DPack.sizeof_DForwarding + 1;
                    sendArray = new NGCArray(bsize + Size);
                    DPack.DForwarding.PackUP(sendArray.Bytes, 1, guarant, ID);
                    sendArray.Offset += bsize;
                }
                else
                {
                    const int bsize = DPack.sizeof_DRPC + 1;
                    sendArray = new NGCArray(bsize + Size);
                    DPack.DRPC.PackUP(sendArray.Bytes, 1, ID);
                    sendArray.Offset += bsize;
                }
                sendArray.Bytes[0] = packetType;

                PackUp(sendArray);
                sendArray.Offset = 0; // Offset использовался как курсор записи; данные начинаются с 0

                await client.Send(sendArray, guarant, token).ConfigureAwait(false);
                if (type == RCType.ReturnWait)
                {
                    var handle = new RCWaitHandle(this);
                    //TODO client.CancellationRun не реализован в AClient иHeadlessHandler
                    CancellationToken tokenPro;
                    if (client is Client real_client)
                    {
                        tokenPro = CancellationTokenSource.CreateLinkedTokenSource(token, real_client.CancellationRun.Token).Token;
                    }
                    else
                    {
                        tokenPro = token;
                    }

                    client.RPCReturn.TryAdd(ID, handle);
                    await handle.Semaphore.WaitAsync(tokenPro).ConfigureAwait(false);
                }
            }
            finally
            {
                sendArray.Dispose();
            }
        }
    }
}