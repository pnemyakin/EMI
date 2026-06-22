using EMI.MyException;
using EMI.NGC;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.Network.NetTCPV3
{
    public class NetTCPV3Client : INetworkClient
    {
        public bool IsConnect
        {
            get;
            private set;
        }

        public event INetworkClientDisconnected Disconnected;

        private NetworkStream NetworkStream;
        private TcpClient TcpClient;
        private readonly NetTCPV3Server Server;
        private const int MaxOneSendSize = 1023;
        private bool IsServerSide => Server != null;
        public int SendByteSpeed { get; private set; } = 0;
        public float DeliveredRate { get; private set; } = 0;
        public RandomDropType RandomDrop { get; set; } = RandomDropType.NoGuaranteed;

        private readonly byte[] AcceptHeaderBuffer = new byte[MessageHeader.SizeOf];
        private readonly byte[] SendHeaderBuffer = new byte[MessageHeader.SizeOf];

        private readonly byte[] AcceptSPHeaderBuffer = new byte[MessageSegmentHeader.SizeOf];
        private readonly byte[] SendSPHeaderBuffer = new byte[MessageSegmentHeader.SizeOf];

        private readonly byte[] AcceptSPElementBuffer = new byte[MessageSegmentElement.SizeOf];
        private readonly byte[] SendSPElementBuffer = new byte[MessageSegmentElement.SizeOf];

        /// <summary>
        /// для выдачи уникального ID сегментного пакета
        /// </summary>
        private uint SegmentPacketID = 0;
        private readonly Dictionary<uint, INGCArray> SegmentPacketsBuffer = new Dictionary<uint, INGCArray>();

        private SemaphoreSlim SemaphoreRead;
        private SemaphoreSlim SemaphoreWrite;

        /// <summary>
        /// Для дропинга пакетов при перегрузке
        /// </summary>
        private readonly Random Rnd = new Random(0);
        /// <summary>
        /// Сколько байт в настоящий момент отправляются
        /// </summary>
        private int SendingBytesCount = 0;

        /// <summary>
        /// Инициализация серверного клиента
        /// </summary>
        /// <param name="server"></param>
        /// <param name="tcpClient"></param>
        internal NetTCPV3Client(NetTCPV3Server server, TcpClient tcpClient)
        {
            lock (this)
            {
                TcpClient = tcpClient;
                NetworkStream = TcpClient.GetStream();
                Server = server;
                Init();
                Disconnected += NetBaseTCPClient_Disconnected;
                IsConnect = true;

                _ = Task.Factory.StartNew(DeliveredRateProcessor, TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// Инициализация клинта
        /// </summary>
        public NetTCPV3Client()
        {
            Disconnected += NetBaseTCPClient_Disconnected;
        }

        private void Init()
        {
            SemaphoreRead = new SemaphoreSlim(1, 1);
            SemaphoreWrite = new SemaphoreSlim(1, 1);
            TcpClient.NoDelay = true;
            TcpClient.ReceiveTimeout = 15;
            TcpClient.SendTimeout = 60000;
            TcpClient.ReceiveBufferSize = MaxOneSendSize * 20;
            TcpClient.SendBufferSize = MaxOneSendSize;
        }

        private readonly object RateLock = new object();
        private int TMPSendSpeed = 0;

        /// <summary>
        /// Отбросить ли пакет (использовать при перегрузке, с вероятность ответит стоит ли отбрасывать пакет)
        /// </summary>
        /// <returns></returns>
        private bool RNDSend()
        {
            return Rnd.NextDouble() - (1.0000001 - DeliveredRate) >= 0;
        }

        private async Task DeliveredRateProcessor()
        {
            int time = 0;
            while (IsConnect)
            {
                await Task.Delay(100).ConfigureAwait(false);
                time++;
                lock (RateLock)
                {
                    if (time >= 10)
                    {
                        time = 0;
                        SendByteSpeed = (SendByteSpeed + TMPSendSpeed) / 2;
                        TMPSendSpeed = 0;
                    }

                    float count = (SendingBytesCount - SendByteSpeed / 5f) / 1000f;
                    if (count < 0)
                        count = 0;

                    float rate = 1 - count;
                    if (rate < 0)
                        rate = 0;

                    if (rate < DeliveredRate)
                        DeliveredRate = rate;
                    else
                        DeliveredRate = (DeliveredRate * 10 + rate) / 11;
                }
            }
        }

        private void UpdateRate(int size)
        {
            lock (RateLock)
            {
                SendingBytesCount += size;

                if (SendingBytesCount < 0)
                    SendingBytesCount = 0;

                if (size < 0)
                {
                    TMPSendSpeed += -size;
                }
            }
        }

        private void NetBaseTCPClient_Disconnected(string error)
        {
            SemaphoreRead.Dispose();
            SemaphoreWrite.Dispose();

            if (!IsServerSide)
            {
                SemaphoreRead = new SemaphoreSlim(1, 1);
                SemaphoreWrite = new SemaphoreSlim(1, 1);
            }
        }

        private async Task AcceptLow(byte[] buffer, int count, CancellationToken token, int offset = 0)
        {
            while (count > 0)
            {
                int size = await NetworkStream.ReadAsync(buffer, offset, count, token).ConfigureAwait(false);
                if (size == 0)
                    throw new System.IO.IOException("Remote side closed the connection");
                count -= size;
                offset += size;
            }
        }

        public void Disconnect(string user_error)
        {
            if (IsConnect)
            {
                IsConnect = false;

                try
                {
                    foreach (var spb in SegmentPacketsBuffer.Values)
                    {
                        try
                        {
                            spb.Dispose();
                        }
                        catch { }
                    }
                }
                catch { }

                if (IsServerSide)
                {
                    lock (Server.TCPClients)
                        Server.TCPClients.Remove(this);
                }
                // Отправляем причину разрыва, затем уведомляем подписчиков и закрываем сокет.
                // Disconnected?.Invoke перенесён ВНУТРЬ Task.Run чтобы гарантировать:
                // SendLow использует ещё живые семафоры, и только потом они уничтожаются в NetBaseTCPClient_Disconnected.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendLow(new EasyArray(
                            System.Text.Encoding.Default.GetBytes(user_error)),
                            PacketHeader.Error, new CancellationTokenSource(1000).Token);
                        await Task.Delay(1500);
                    }
                    catch
                    {
                        // Отправка ошибки не удалась — соединение уже мертво, это ожидаемо
                    }
                    finally
                    {
                        Disconnected?.Invoke(user_error);
                        try
                        {
                            TcpClient.Close();
                        }
                        catch { }
                    }
                });
            }
        }

        public async Task<bool> Connect(string address, CancellationToken token)
        {
            if (IsServerSide)
            {
                throw new NotSupportedException();
            }
            else if (IsConnect)
            {
                throw new AlreadyException("Client is already connected!");
            }
            else
            {
                TcpClient = new TcpClient();
                Init();

                var ep = Utilities.ParseIPAddress(address);
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var connectThread = new Thread(() =>
                {
                    try
                    {
                        TcpClient.Connect(ep);
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                })
                { IsBackground = true };
                connectThread.Start();

                using (token.Register(() => { try { TcpClient.Close(); } catch { } }))
                {
                    try
                    {
                        await tcs.Task.ConfigureAwait(false);
                    }
                    catch (SocketException) when (token.IsCancellationRequested)
                    {
                        try { TcpClient.Close(); } catch { }
                        IsConnect = false;
                        return false;
                    }
                    catch (Exception e) when (token.IsCancellationRequested)
                    {
                        try { TcpClient.Close(); } catch { }
                        Disconnected?.Invoke(e.ToString());
                        IsConnect = false;
                        return false;
                    }
                }

                if (token.IsCancellationRequested)
                {
                    try { TcpClient.Close(); } catch { }
                    IsConnect = false;
                    return false;
                }

                NetworkStream = TcpClient.GetStream();
                IsConnect = true;
                _ = Task.Factory.StartNew(DeliveredRateProcessor, TaskCreationOptions.LongRunning);
                return true;
            }
        }

        private async Task SendLow(INGCArray array, PacketHeader flags, CancellationToken token)
        {
            if (array.Length < MaxOneSendSize)
            {
                await SemaphoreWrite.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    MessageHeader header = new MessageHeader((ushort)array.Length, flags);
                    header.WriteToBuffer(SendHeaderBuffer);
                    UpdateRate(array.Length);

                    await NetworkStream.WriteAsync(SendHeaderBuffer, 0, SendHeaderBuffer.Length, token).ConfigureAwait(false);
                    await NetworkStream.WriteAsync(array.Bytes, 0, array.Length, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    if (!flags.HasFlag(PacketHeader.Error))
                    {
                        Disconnect(e.Message);
                    }
                }
                finally
                {
                    try
                    {
                        SemaphoreWrite.Release();
                    }
                    catch { }
                }
            }
            else
            {
                if(flags.HasFlag(PacketHeader.Error))
                    throw new ClientViolationRightsException($"The error message is too large!");

                try
                {
                    MessageSegmentHeader segmentHeader;
                    await SemaphoreWrite.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        var header = new MessageHeader(MaxOneSendSize, PacketHeader.SegmentPacketHeader);
                        header.WriteToBuffer(SendHeaderBuffer);
                        UpdateRate(MaxOneSendSize);

                        segmentHeader = new MessageSegmentHeader(SegmentPacketID++, array.Length);
                        segmentHeader.WriteToBuffer(SendSPHeaderBuffer);

                        await NetworkStream.WriteAsync(SendHeaderBuffer, 0, SendHeaderBuffer.Length, token).ConfigureAwait(false);
                        await NetworkStream.WriteAsync(SendSPHeaderBuffer, 0, SendSPHeaderBuffer.Length, token).ConfigureAwait(false);

                        await NetworkStream.WriteAsync(array.Bytes, 0, MaxOneSendSize, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        SemaphoreWrite.Release();
                    }

                    for (array.Offset = MaxOneSendSize; array.Offset < array.Length; array.Offset += MaxOneSendSize)
                    {
                        await SemaphoreWrite.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            int size = Math.Min(array.Length - array.Offset, MaxOneSendSize);
                            var header = new MessageHeader((ushort)size, PacketHeader.SegmentPacketElement);
                            header.WriteToBuffer(SendHeaderBuffer);
                            UpdateRate(size);

                            var segmentElement = new MessageSegmentElement(segmentHeader.ID);
                            segmentElement.WriteToBuffer(SendSPElementBuffer);

                            await NetworkStream.WriteAsync(SendHeaderBuffer, 0, SendHeaderBuffer.Length, token).ConfigureAwait(false);
                            await NetworkStream.WriteAsync(SendSPElementBuffer, 0, SendSPElementBuffer.Length, token).ConfigureAwait(false);

                            await NetworkStream.WriteAsync(array.Bytes, array.Offset, size, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            SemaphoreWrite.Release();
                        }
                    }
                }
                catch (Exception e)
                {
                    Disconnect(e.Message);
                }
            }
        }

        private async Task SendLowDone(MessageHeader header, CancellationToken token)
        {
            await SemaphoreWrite.WaitAsync(token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
                return;
            try
            {
                header.Size = (ushort)header.GetSize();
                header.Flags |= PacketHeader.PacketDone;

                header.WriteToBuffer(SendHeaderBuffer);
                await NetworkStream.WriteAsync(SendHeaderBuffer, 0, SendHeaderBuffer.Length, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Disconnect(e.Message);
            }
            finally
            {
                SemaphoreWrite.Release();
            }
        }

        public async Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            if (RandomDrop == RandomDropType.Nothing)
            {
                await SendLow(array, PacketHeader.None, token).ConfigureAwait(false);
            }
            else if (RandomDrop == RandomDropType.NoGuaranteed)
            {
                if (guaranteed)
                {
                    await SendLow(array, PacketHeader.None, token).ConfigureAwait(false);
                }
                else if (DeliveredRate > 0.99 || RNDSend())
                {
                    await SendLow(array, PacketHeader.None, token).ConfigureAwait(false);
                }
            }
            else
            {
                if (guaranteed)
                {
                    while(DeliveredRate < 0.99 && !RNDSend())
                    {
                        await Task.Delay(50).ConfigureAwait(false);
                    }

                    await SendLow(array, PacketHeader.None, token).ConfigureAwait(false);
                }
                else if (DeliveredRate > 0.99 || RNDSend())
                {
                    await SendLow(array, PacketHeader.None, token).ConfigureAwait(false);
                }
            }
        }

        public async Task<INGCArray> AcceptPacket(int max_size, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await SemaphoreRead.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await AcceptLow(AcceptHeaderBuffer, MessageHeader.SizeOf, token).ConfigureAwait(false);

                    var header = MessageHeader.FromBytes(AcceptHeaderBuffer);
                    int size = (int)header.GetSize();
                    PacketHeader flags = header.Flags;

                    if(!flags.HasFlag(PacketHeader.PacketDone))
                    {
                        _ = SendLowDone(header, token);
                    }

                    if (flags.HasFlag(PacketHeader.SegmentPacketHeader))
                    {
                        await AcceptLow(AcceptSPHeaderBuffer, MessageSegmentHeader.SizeOf, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                            return INGCArrayUtils.EmptyArray;

                        var spheader = MessageSegmentHeader.FromBytes(AcceptSPHeaderBuffer);
                        if(spheader.Size > max_size)
                        {
                            throw new ClientViolationRightsException($"The allowed buffer ({size} > {max_size}) for reading an incoming packet has been exceeded!");
                        }
                        SegmentPacketsBuffer.Add(spheader.ID, new NGCArray(spheader.Size));

                        try
                        {
                            var buf = SegmentPacketsBuffer[spheader.ID];
                            await AcceptLow(buf.Bytes, size, token).ConfigureAwait(false);
                            buf.Offset += size;
                            SegmentPacketsBuffer[spheader.ID] = buf;

                            if (token.IsCancellationRequested)
                                return INGCArrayUtils.EmptyArray;
                        }
                        catch (Exception e)
                        {
                            Disconnect(e.Message);
                            return INGCArrayUtils.EmptyArray;
                        }
                    }
                    else if (flags.HasFlag(PacketHeader.SegmentPacketElement))
                    {
                        await AcceptLow(AcceptSPElementBuffer, MessageSegmentElement.SizeOf, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                            return INGCArrayUtils.EmptyArray;

                        var spelement = MessageSegmentElement.FromBytes(AcceptSPElementBuffer);

                        try
                        {
                            var buf = SegmentPacketsBuffer[spelement.ID];

                            await AcceptLow(buf.Bytes, size, token, buf.Offset).ConfigureAwait(false);
                            buf.Offset += size;
                            SegmentPacketsBuffer[spelement.ID] = buf;

                            if (token.IsCancellationRequested)
                                return INGCArrayUtils.EmptyArray;

                            if (buf.Offset == buf.Length)
                            {
                                buf.Offset = 0;
                                SegmentPacketsBuffer.Remove(spelement.ID);
                                return buf;
                            }
                        }
                        catch (Exception e)
                        {
                            Disconnect(e.Message);
                            return INGCArrayUtils.EmptyArray;
                        }
                    }
                    else if (flags.HasFlag(PacketHeader.PacketDone))
                    {
                        UpdateRate(-size);
                    }
                    else if (flags.HasFlag(PacketHeader.Error))
                    {
                        INGCArray array = new NGCArray(size);
                        try
                        {
                            await AcceptLow(array.Bytes, array.Length, token).ConfigureAwait(false);

                            if (token.IsCancellationRequested)
                            {
                                throw new ClientDisconnectException($"The remote client broke the connection by sending a message - none");
                            }
                            else
                            {
                                string message = System.Text.Encoding.Default.GetString(array.Bytes, array.Offset, array.Length);
                                throw new ClientDisconnectException($"The remote client broke the connection by sending a message - {message}");
                            }
                        }
                        catch (Exception e)
                        {
                            array.Dispose();
                            Disconnect(e.Message);
                            return INGCArrayUtils.EmptyArray;
                        }
                        finally
                        {
                            array.Dispose();
                        }
                    }
                    else
                    {
                        INGCArray array = new NGCArray(size);
                        try
                        {
                            await AcceptLow(array.Bytes, array.Length, token).ConfigureAwait(false);

                            if (token.IsCancellationRequested)
                            {
                                array.Dispose();
                                return INGCArrayUtils.EmptyArray;
                            }
                            else
                            {
                                return array;
                            }
                        }
                        catch (Exception e)
                        {
                            array.Dispose();
                            Disconnect(e.Message);
                            return INGCArrayUtils.EmptyArray;
                        }
                    }
                }
                catch (Exception e)
                {
                    Disconnect(e.Message);
                    return INGCArrayUtils.EmptyArray;
                }
                finally
                {
                    try
                    {
                        SemaphoreRead.Release();
                    }
                    catch { }
                }
            }

            return INGCArrayUtils.EmptyArray;
        }

        public string GetRemoteClientAddress()
        {
            try
            {
                var addr = TcpClient.Client.RemoteEndPoint as IPEndPoint;
                return $"{addr.Address}#{addr.Port}";
            }
            catch
            {
                return "none";
            }
        }
    }
}