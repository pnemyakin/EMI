using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EMI.MyException;
using EMI.Network;
using EMI.NGC;

namespace EMI.NetUDP
{
    /// <summary>
    /// UDP клиент — реализует INetworkClient с поддержкой надёжной и ненадёжной доставки
    /// </summary>
    public class NetUDPClient : INetworkClient
    {
        #region INetworkClient Properties

        public bool IsConnect { get; private set; }
        public int SendByteSpeed { get; private set; } = 0;
        public float DeliveredRate { get; private set; } = 1f;
        public RandomDropType RandomDrop { get; set; } = RandomDropType.NoGuaranteed;
        public event INetworkClientDisconnected Disconnected;

        #endregion

        #region Network

        /// <summary>
        /// Сокет (клиентский — собственный, серверный — общий с сервером)
        /// </summary>
        private UdpClient Socket;

        /// <summary>
        /// Адрес удалённой стороны
        /// </summary>
        private IPEndPoint RemoteEndPoint;

        /// <summary>
        /// Ссылка на сервер (null для клиентского подключения)
        /// </summary>
        private readonly NetUDPServer Server;
        private bool IsServerSide => Server != null;

        #endregion

        #region Reliability State

        /// <summary>
        /// Следующий sequence для отправки надёжных пакетов
        /// </summary>
        private ushort SendSequence = 0;

        /// <summary>
        /// Последний полученный sequence от удалённой стороны
        /// </summary>
        private ushort RemoteSequence = 0;

        /// <summary>
        /// Получен ли хотя бы один надёжный пакет
        /// </summary>
        private bool HasReceivedReliable = false;

        /// <summary>
        /// Битовое поле: какие из предыдущих 32 пакетов получены
        /// </summary>
        private uint AckBitfield = 0;

        /// <summary>
        /// Буфер ожидающих подтверждения надёжных пакетов
        /// </summary>
        private readonly Dictionary<ushort, PendingPacket> PendingReliable = new Dictionary<ushort, PendingPacket>();
        private readonly object PendingLock = new object();

        /// <summary>
        /// Захват для последовательной отправки
        /// </summary>
        private SemaphoreSlim SendSemaphore;

        #endregion

        #region Fragmentation

        /// <summary>
        /// Следующий ID фрагмента
        /// </summary>
        private ushort NextFragmentID = 0;

        /// <summary>
        /// Буферы сборки фрагментов
        /// </summary>
        private readonly Dictionary<ushort, FragmentAssembly> FragmentBuffers = new Dictionary<ushort, FragmentAssembly>();
        private readonly object FragmentLock = new object();

        #endregion

        #region Receive Queue

        /// <summary>
        /// Очередь собранных пакетов для обработки верхним уровнем
        /// </summary>
        private Channel<INGCArray> ReceiveQueue;

        #endregion

        #region Rate Tracking

        private readonly object RateLock = new object();
        private int TMPSendSpeed = 0;
        private int SendingBytesCount = 0;
        private long LastReceivedTicks = 0;

        /// <summary>
        /// Для дропинга пакетов при перегрузке
        /// </summary>
        private readonly Random Rnd = new Random(0);

        #endregion

        #region Pre-allocated Buffers

        /// <summary>
        /// Буфер для отправки ACK пакетов (7 байт, переиспользуется)
        /// </summary>
        private readonly byte[] AckSendBuffer = new byte[UDPAckPacket.SizeOf];

        /// <summary>
        /// Буфер для отправки пинг/понг пакетов (3 байта)
        /// </summary>
        private readonly byte[] PingSendBuffer = new byte[UDPPingPacket.SizeOf];

        /// <summary>
        /// Буфер для конструирования отправляемых дейтаграмм (MTU)
        /// </summary>
        private readonly byte[] DatagramSendBuffer = new byte[UDPConstants.MTU];

        #endregion

        #region Constructors

        /// <summary>
        /// Клиентская инициализация (для исходящего подключения)
        /// </summary>
        public NetUDPClient()
        {
            Server = null;
        }

        /// <summary>
        /// Серверная инициализация (сервер создаёт для принятого подключения)
        /// </summary>
        internal NetUDPClient(NetUDPServer server, IPEndPoint remoteEP)
        {
            Server = server;
            RemoteEndPoint = remoteEP;
            IsConnect = true;
            InitConnection();
            _ = Task.Factory.StartNew(RetransmitLoop, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(DeliveredRateProcessor, TaskCreationOptions.LongRunning);
            _ = Task.Factory.StartNew(InternalPingLoop, TaskCreationOptions.LongRunning);
        }

        #endregion

        #region Connection

        public async Task<bool> Connect(string address, CancellationToken token)
        {
            if (IsServerSide)
                throw new NotSupportedException();

            if (IsConnect)
                throw new AlreadyException("Client is already connected!");

            try
            {
                var ep = Utilities.ParseIPAddress(address);
                Socket = new UdpClient();
                Socket.Connect(ep);
                RemoteEndPoint = ep;

                // Создаём токен подключения
                ulong connectionToken = (ulong)DateTime.UtcNow.Ticks ^ (ulong)Rnd.Next();

                // Отправляем ConnectionRequest
                var connPacket = new UDPConnectionPacket(UDPPacketType.ConnectionRequest, connectionToken);
                var buffer = new byte[UDPConnectionPacket.SizeOf];
                connPacket.WriteToBuffer(buffer);

                // Пробуем несколько раз с ожиданием ответа
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    if (token.IsCancellationRequested)
                        break;

                    await Socket.SendAsync(buffer, buffer.Length).ConfigureAwait(false);

                    // Ждём ответ с таймаутом
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(500);

                    try
                    {
                        var result = await ReceiveWithTimeout(cts.Token).ConfigureAwait(false);
                        if (result.HasValue && result.Value.Buffer.Length >= UDPConnectionPacket.SizeOf)
                        {
                            var type = (UDPPacketType)result.Value.Buffer[0];
                            if (type == UDPPacketType.ConnectionAccept)
                            {
                                var acceptPacket = UDPConnectionPacket.FromBytes(result.Value.Buffer);
                                if (acceptPacket.Token == connectionToken)
                                {
                                    // Подключение установлено
                                    IsConnect = true;
                                    InitConnection();

                                    _ = Task.Factory.StartNew(ClientReceiveLoop, TaskCreationOptions.LongRunning);
                                    _ = Task.Factory.StartNew(RetransmitLoop, TaskCreationOptions.LongRunning);
                                    _ = Task.Factory.StartNew(DeliveredRateProcessor, TaskCreationOptions.LongRunning);
                                    _ = Task.Factory.StartNew(InternalPingLoop, TaskCreationOptions.LongRunning);

                                    return true;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Таймаут — повторяем
                    }
                }

                // Не удалось подключиться
                try { Socket?.Close(); } catch { }
                Socket = null;
                return false;
            }
            catch (Exception e)
            {
                try { Socket?.Close(); } catch { }
                Socket = null;
                Disconnected?.Invoke(e.ToString());
                return false;
            }
        }

        private void InitConnection()
        {
            SendSequence = 0;
            RemoteSequence = 0;
            HasReceivedReliable = false;
            AckBitfield = 0;
            NextFragmentID = 0;
            SendingBytesCount = 0;
            TMPSendSpeed = 0;
            SendByteSpeed = 0;
            DeliveredRate = 1f;
            LastReceivedTicks = DateTime.UtcNow.Ticks;

            SendSemaphore = new SemaphoreSlim(1, 1);
            ReceiveQueue = Channel.CreateBounded<INGCArray>(UDPConstants.ReceiveQueueCapacity);

            lock (PendingLock)
                PendingReliable.Clear();

            lock (FragmentLock)
            {
                foreach (var fb in FragmentBuffers.Values)
                    fb.Dispose();
                FragmentBuffers.Clear();
            }
        }

        public void Disconnect(string user_error)
        {
            if (!IsConnect)
                return;

            IsConnect = false;

            // Пытаемся отправить Disconnect пакет
            try
            {
                byte[] reasonBytes = System.Text.Encoding.UTF8.GetBytes(user_error ?? "");

                int len = 1 + reasonBytes.Length;
                byte[] packet = new byte[len];
                packet[0] = (byte)UDPPacketType.Disconnect;
                if (reasonBytes.Length > 0)
                    Array.Copy(reasonBytes, 0, packet, 1, reasonBytes.Length);

                SendRaw(packet, len);
            }
            catch { }

            // Очистка ресурсов
            CleanupConnection();
            Disconnected?.Invoke(user_error);
        }

        private void CleanupConnection()
        {
            lock (PendingLock)
            {
                PendingReliable.Clear();
            }

            lock (FragmentLock)
            {
                foreach (var fb in FragmentBuffers.Values)
                    fb.Dispose();
                FragmentBuffers.Clear();
            }

            if (!IsServerSide)
            {
                try { Socket?.Close(); } catch { }
            }
            else
            {
                Server?.RemoveClient(this, RemoteEndPoint);
            }

            try { ReceiveQueue?.Writer.TryComplete(); } catch { }

            try { SendSemaphore?.Dispose(); } catch { }
            SendSemaphore = new SemaphoreSlim(1, 1);
        }

        #endregion

        #region Send

        public async Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            if (!IsConnect)
                return;

            // Проверка RandomDrop
            if (!ShouldSend(guaranteed))
                return;

            if (guaranteed)
            {
                await SendReliable(array, token).ConfigureAwait(false);
            }
            else
            {
                await SendUnreliable(array, token).ConfigureAwait(false);
            }
        }

        private async Task SendUnreliable(INGCArray array, CancellationToken token)
        {
            if (array.Length <= UDPConstants.MaxUnreliablePayload)
            {
                // Один пакет — без фрагментации
                await SendSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    DatagramSendBuffer[0] = (byte)UDPPacketType.UnreliableData;
                    Array.Copy(array.Bytes, 0, DatagramSendBuffer, 1, array.Length);
                    int totalLen = 1 + array.Length;

                    UpdateRateSend(totalLen);
                    SendRaw(DatagramSendBuffer, totalLen);
                }
                finally
                {
                    try { SendSemaphore.Release(); } catch { }
                }
            }
            else
            {
                // Фрагментация
                await SendFragmented(array, false, token).ConfigureAwait(false);
            }
        }

        private async Task SendReliable(INGCArray array, CancellationToken token)
        {
            if (array.Length <= UDPConstants.MaxReliablePayload)
            {
                // Один надёжный пакет
                ushort seq;
                byte[] packetData;

                await SendSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    seq = SendSequence++;

                    int totalLen = UDPReliableHeader.SizeOf + array.Length;
                    packetData = new byte[totalLen];

                    var header = new UDPReliableHeader(UDPPacketType.ReliableData, seq);
                    header.WriteToBuffer(packetData);
                    Array.Copy(array.Bytes, 0, packetData, UDPReliableHeader.SizeOf, array.Length);

                    // Сохраняем для ретрансляции
                    lock (PendingLock)
                    {
                        PendingReliable[seq] = new PendingPacket(packetData, totalLen);
                    }

                    UpdateRateSend(totalLen);
                    SendRaw(packetData, totalLen);
                }
                finally
                {
                    try { SendSemaphore.Release(); } catch { }
                }
            }
            else
            {
                // Фрагментация с надёжной доставкой
                await SendFragmented(array, true, token).ConfigureAwait(false);
            }
        }

        private async Task SendFragmented(INGCArray array, bool reliable, CancellationToken token)
        {
            int maxPayload = reliable
                ? UDPConstants.MaxReliableFragmentPayload
                : UDPConstants.MaxUnreliableFragmentPayload;

            int fragmentCount = (array.Length + maxPayload - 1) / maxPayload;
            if (fragmentCount > UDPConstants.MaxFragmentCount)
                throw new ClientViolationRightsException($"Message too large for fragmentation: {array.Length} bytes, max {(long)UDPConstants.MaxFragmentCount * maxPayload}");

            ushort fragId;
            await SendSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                fragId = NextFragmentID++;
            }
            finally
            {
                try { SendSemaphore.Release(); } catch { }
            }

            for (int i = 0; i < fragmentCount; i++)
            {
                if (token.IsCancellationRequested || !IsConnect)
                    return;

                int offset = i * maxPayload;
                int payloadLen = Math.Min(maxPayload, array.Length - offset);

                if (reliable)
                {
                    await SendSemaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        ushort seq = SendSequence++;

                        int totalLen = UDPReliableHeader.SizeOf + UDPFragmentHeader.SizeOf + payloadLen;
                        byte[] packetData = new byte[totalLen];

                        var header = new UDPReliableHeader(UDPPacketType.ReliableFragment, seq);
                        header.WriteToBuffer(packetData);

                        var fragHeader = new UDPFragmentHeader(fragId, (ushort)i, (ushort)fragmentCount);
                        fragHeader.WriteToBuffer(packetData, UDPReliableHeader.SizeOf);

                        Array.Copy(array.Bytes, offset, packetData,
                            UDPReliableHeader.SizeOf + UDPFragmentHeader.SizeOf, payloadLen);

                        lock (PendingLock)
                        {
                            PendingReliable[seq] = new PendingPacket(packetData, totalLen);
                        }

                        UpdateRateSend(totalLen);
                        SendRaw(packetData, totalLen);
                    }
                    finally
                    {
                        try { SendSemaphore.Release(); } catch { }
                    }
                }
                else
                {
                    await SendSemaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        int totalLen = 1 + UDPFragmentHeader.SizeOf + payloadLen;

                        DatagramSendBuffer[0] = (byte)UDPPacketType.UnreliableFragment;
                        var fragHeader = new UDPFragmentHeader(fragId, (ushort)i, (ushort)fragmentCount);
                        fragHeader.WriteToBuffer(DatagramSendBuffer, 1);

                        Array.Copy(array.Bytes, offset, DatagramSendBuffer,
                            1 + UDPFragmentHeader.SizeOf, payloadLen);

                        UpdateRateSend(totalLen);
                        SendRaw(DatagramSendBuffer, totalLen);
                    }
                    finally
                    {
                        try { SendSemaphore.Release(); } catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Низкоуровневая отправка дейтаграммы
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendRaw(byte[] data, int length)
        {
            try
            {
                if (IsServerSide)
                {
                    Server.SendTo(data, length, RemoteEndPoint);
                }
                else
                {
                    Socket?.Send(data, length);
                }
            }
            catch (Exception e)
            {
                Disconnect(e.Message);
            }
        }

        private void SendAck()
        {
            var ack = new UDPAckPacket(RemoteSequence, AckBitfield);
            ack.WriteToBuffer(AckSendBuffer);
            SendRaw(AckSendBuffer, UDPAckPacket.SizeOf);
        }

        #endregion

        #region Receive

        public async Task<INGCArray> AcceptPacket(int max_size, CancellationToken token)
        {
            try
            {
                var result = await ReceiveQueue.Reader.ReadAsync(token).ConfigureAwait(false);
                if (result.Length > max_size)
                {
                    result.Dispose();
                    throw new ClientViolationRightsException($"Packet too large: {result.Length} > {max_size}");
                }
                return result;
            }
            catch (ChannelClosedException)
            {
                return INGCArrayUtils.EmptyArray;
            }
            catch (OperationCanceledException)
            {
                return INGCArrayUtils.EmptyArray;
            }
        }

        /// <summary>
        /// Цикл приёма для клиентского (неserверного) подключения
        /// </summary>
        private async Task ClientReceiveLoop()
        {
            while (IsConnect)
            {
                try
                {
                    var result = await Socket.ReceiveAsync().ConfigureAwait(false);
                    if (result.Buffer != null && result.Buffer.Length > 0)
                    {
                        ProcessIncomingDatagram(result.Buffer, result.Buffer.Length);
                    }
                }
                catch
                {
                    if (IsConnect)
                    {
                        Disconnect("Receive error");
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Обрабатывает входящую дейтаграмму (вызывается из цикла приёма или из сервера)
        /// </summary>
        internal void ProcessIncomingDatagram(byte[] data, int length)
        {
            if (length < 1 || !IsConnect)
                return;

            LastReceivedTicks = DateTime.UtcNow.Ticks;

            var type = (UDPPacketType)data[0];

            switch (type)
            {
                case UDPPacketType.UnreliableData:
                    HandleUnreliableData(data, length);
                    break;

                case UDPPacketType.ReliableData:
                    HandleReliableData(data, length);
                    break;

                case UDPPacketType.UnreliableFragment:
                    HandleUnreliableFragment(data, length);
                    break;

                case UDPPacketType.ReliableFragment:
                    HandleReliableFragment(data, length);
                    break;

                case UDPPacketType.Ack:
                    HandleAck(data, length);
                    break;

                case UDPPacketType.Disconnect:
                    HandleDisconnect(data, length);
                    break;

                case UDPPacketType.Ping:
                    HandlePing(data, length);
                    break;

                case UDPPacketType.Pong:
                    HandlePong(data, length);
                    break;
            }
        }

        private void HandleUnreliableData(byte[] data, int length)
        {
            int payloadLen = length - 1;
            if (payloadLen <= 0)
                return;

            var ngcArray = new NGCArray(payloadLen);
            Array.Copy(data, 1, ngcArray.Bytes, 0, payloadLen);

            if (!ReceiveQueue.Writer.TryWrite(ngcArray))
            {
                ngcArray.Dispose();
            }
        }

        private void HandleReliableData(byte[] data, int length)
        {
            if (length < UDPReliableHeader.SizeOf)
                return;

            var header = UDPReliableHeader.FromBytes(data);
            int payloadLen = length - UDPReliableHeader.SizeOf;

            // Обновляем ACK-состояние
            UpdateReceivedSequence(header.Sequence);
            SendAck();

            if (payloadLen <= 0)
                return;

            var ngcArray = new NGCArray(payloadLen);
            Array.Copy(data, UDPReliableHeader.SizeOf, ngcArray.Bytes, 0, payloadLen);

            if (!ReceiveQueue.Writer.TryWrite(ngcArray))
            {
                ngcArray.Dispose();
            }
        }

        private void HandleUnreliableFragment(byte[] data, int length)
        {
            if (length < 1 + UDPFragmentHeader.SizeOf)
                return;

            var fragHeader = UDPFragmentHeader.FromBytes(data, 1);
            int payloadOffset = 1 + UDPFragmentHeader.SizeOf;
            int payloadLen = length - payloadOffset;

            if (payloadLen <= 0)
                return;

            AssembleFragment(fragHeader, data, payloadOffset, payloadLen);
        }

        private void HandleReliableFragment(byte[] data, int length)
        {
            if (length < UDPReliableHeader.SizeOf + UDPFragmentHeader.SizeOf)
                return;

            var header = UDPReliableHeader.FromBytes(data);

            // Обновляем ACK-состояние
            UpdateReceivedSequence(header.Sequence);
            SendAck();

            var fragHeader = UDPFragmentHeader.FromBytes(data, UDPReliableHeader.SizeOf);
            int payloadOffset = UDPReliableHeader.SizeOf + UDPFragmentHeader.SizeOf;
            int payloadLen = length - payloadOffset;

            if (payloadLen <= 0)
                return;

            AssembleFragment(fragHeader, data, payloadOffset, payloadLen);
        }

        private void AssembleFragment(UDPFragmentHeader fragHeader, byte[] data, int payloadOffset, int payloadLen)
        {
            lock (FragmentLock)
            {
                if (!FragmentBuffers.TryGetValue(fragHeader.FragmentID, out var assembly))
                {
                    assembly = new FragmentAssembly(fragHeader.FragmentCount);
                    FragmentBuffers[fragHeader.FragmentID] = assembly;
                }

                assembly.AddFragment(fragHeader.FragmentIndex, data, payloadOffset, payloadLen);

                if (assembly.IsComplete)
                {
                    var assembled = assembly.BuildMessage();
                    FragmentBuffers.Remove(fragHeader.FragmentID);

                    if (!ReceiveQueue.Writer.TryWrite(assembled))
                    {
                        assembled.Dispose();
                    }
                }
            }
        }

        private void HandleAck(byte[] data, int length)
        {
            if (length < UDPAckPacket.SizeOf)
                return;

            var ack = UDPAckPacket.FromBytes(data);
            ProcessAck(ack.Sequence, ack.AckBitfield);
        }

        private void HandleDisconnect(byte[] data, int length)
        {
            string reason = "Remote disconnect";
            if (length > 1)
            {
                try
                {
                    reason = System.Text.Encoding.UTF8.GetString(data, 1, length - 1);
                }
                catch { }
            }

            if (IsConnect)
            {
                IsConnect = false;
                CleanupConnection();
                Disconnected?.Invoke(reason);
            }
        }

        private void HandlePing(byte[] data, int length)
        {
            if (length < UDPPingPacket.SizeOf)
                return;

            var ping = UDPPingPacket.FromBytes(data);

            // Отвечаем Pong
            var pong = new UDPPingPacket(UDPPacketType.Pong, ping.PingID);
            pong.WriteToBuffer(PingSendBuffer);
            SendRaw(PingSendBuffer, UDPPingPacket.SizeOf);
        }

        private void HandlePong(byte[] data, int length)
        {
            // Pong получен — LastReceivedTicks уже обновлён
        }

        #endregion

        #region Reliability Layer

        /// <summary>
        /// Обновляет состояние ACK при получении нового sequence
        /// </summary>
        private void UpdateReceivedSequence(ushort sequence)
        {
            if (!HasReceivedReliable)
            {
                HasReceivedReliable = true;
                RemoteSequence = sequence;
                AckBitfield = 0;
                return;
            }

            int diff = SequenceDiff(sequence, RemoteSequence);

            if (diff > 0)
            {
                // Более новый пакет — сдвигаем битовое поле
                if (diff <= UDPConstants.AckBitfieldSize)
                {
                    AckBitfield = (AckBitfield << diff) | (1u << (diff - 1));
                }
                else
                {
                    AckBitfield = 0;
                }
                RemoteSequence = sequence;
            }
            else if (diff < 0)
            {
                // Пакет из прошлого — помечаем в битовом поле
                int bitIndex = -diff - 1;
                if (bitIndex < UDPConstants.AckBitfieldSize)
                {
                    AckBitfield |= (1u << bitIndex);
                }
            }
            // diff == 0: дубликат, игнорируем
        }

        /// <summary>
        /// Обрабатывает полученное подтверждение
        /// </summary>
        private void ProcessAck(ushort ackSequence, uint ackBitfield)
        {
            lock (PendingLock)
            {
                // Подтверждаем основной sequence
                if (PendingReliable.Remove(ackSequence))
                {
                    UpdateRateAck(ackSequence);
                }

                // Подтверждаем пакеты из битового поля
                for (int i = 0; i < UDPConstants.AckBitfieldSize; i++)
                {
                    if ((ackBitfield & (1u << i)) != 0)
                    {
                        ushort seq = (ushort)(ackSequence - 1 - i);
                        if (PendingReliable.Remove(seq))
                        {
                            UpdateRateAck(seq);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Вычисляет разницу sequence с учётом переполнения (wraparound)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SequenceDiff(ushort s1, ushort s2)
        {
            int diff = s1 - s2;
            if (diff > 32768) diff -= 65536;
            else if (diff < -32768) diff += 65536;
            return diff;
        }

        /// <summary>
        /// Цикл ретрансляции ненайденных ACK
        /// </summary>
        private async Task RetransmitLoop()
        {
            while (IsConnect)
            {
                await Task.Delay(UDPConstants.InitialRetransmitIntervalMs).ConfigureAwait(false);

                List<PendingPacket> toRetransmit = null;

                lock (PendingLock)
                {
                    long nowTicks = DateTime.UtcNow.Ticks;
                    List<ushort> toRemove = null;

                    foreach (var kv in PendingReliable)
                    {
                        var pending = kv.Value;
                        long elapsed = (nowTicks - pending.SentTicks) / TimeSpan.TicksPerMillisecond;

                        if (elapsed >= pending.RetransmitIntervalMs)
                        {
                            if (pending.Attempts >= UDPConstants.MaxRetransmitAttempts)
                            {
                                // Слишком много попыток — отключаемся
                                if (toRemove == null) toRemove = new List<ushort>();
                                toRemove.Add(kv.Key);

                                Task.Run(() => Disconnect("Reliable packet delivery timeout"));
                                return;
                            }

                            pending.Attempts++;
                            pending.SentTicks = nowTicks;
                            // Экспоненциальный backoff
                            pending.RetransmitIntervalMs = Math.Min(
                                pending.RetransmitIntervalMs * 2,
                                UDPConstants.MaxRetransmitIntervalMs);

                            if (toRetransmit == null) toRetransmit = new List<PendingPacket>();
                            toRetransmit.Add(pending);
                        }
                    }

                    if (toRemove != null)
                    {
                        foreach (var key in toRemove)
                            PendingReliable.Remove(key);
                    }
                }

                // Ретранслируем вне лока
                if (toRetransmit != null)
                {
                    foreach (var pending in toRetransmit)
                    {
                        SendRaw(pending.Data, pending.Length);
                    }
                }
            }
        }

        /// <summary>
        /// Внутренний пинг для обнаружения обрыва соединения
        /// </summary>
        private async Task InternalPingLoop()
        {
            ushort pingId = 0;
            while (IsConnect)
            {
                await Task.Delay(UDPConstants.InternalPingIntervalMs).ConfigureAwait(false);

                // Проверяем таймаут
                long elapsed = (DateTime.UtcNow.Ticks - LastReceivedTicks) / TimeSpan.TicksPerMillisecond;
                if (elapsed > UDPConstants.ConnectionTimeoutMs)
                {
                    Disconnect("Connection timeout");
                    return;
                }

                // Отправляем пинг
                var ping = new UDPPingPacket(UDPPacketType.Ping, pingId++);
                ping.WriteToBuffer(PingSendBuffer);
                SendRaw(PingSendBuffer, UDPPingPacket.SizeOf);
            }
        }

        #endregion

        #region Fragment Assembly

        /// <summary>
        /// Очистка устаревших фрагментов
        /// </summary>
        private void CleanupStaleFragments()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            lock (FragmentLock)
            {
                List<ushort> toRemove = null;
                foreach (var kv in FragmentBuffers)
                {
                    long elapsed = (nowTicks - kv.Value.CreatedTicks) / TimeSpan.TicksPerMillisecond;
                    if (elapsed > UDPConstants.FragmentTimeoutMs)
                    {
                        if (toRemove == null) toRemove = new List<ushort>();
                        toRemove.Add(kv.Key);
                    }
                }

                if (toRemove != null)
                {
                    foreach (var key in toRemove)
                    {
                        if (FragmentBuffers.TryGetValue(key, out var fb))
                        {
                            fb.Dispose();
                            FragmentBuffers.Remove(key);
                        }
                    }
                }
            }
        }

        #endregion

        #region Rate Control

        private bool ShouldSend(bool guaranteed)
        {
            if (RandomDrop == RandomDropType.Nothing)
                return true;

            if (RandomDrop == RandomDropType.NoGuaranteed)
            {
                if (guaranteed)
                    return true;
                return DeliveredRate > 0.99f || RNDSend();
            }

            // RandomDropType.All
            if (guaranteed)
            {
                // Для гарантированных — ждём улучшения
                return true;
            }
            return DeliveredRate > 0.99f || RNDSend();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool RNDSend()
        {
            return Rnd.NextDouble() - (1.0000001 - DeliveredRate) >= 0;
        }

        private void UpdateRateSend(int size)
        {
            lock (RateLock)
            {
                SendingBytesCount += size;
                if (SendingBytesCount < 0)
                    SendingBytesCount = 0;
            }
        }

        private void UpdateRateAck(ushort seq)
        {
            lock (RateLock)
            {
                TMPSendSpeed += 1;
            }
        }

        private async Task DeliveredRateProcessor()
        {
            int time = 0;
            while (IsConnect)
            {
                await Task.Delay(100).ConfigureAwait(false);
                time++;

                // Периодически чистим устаревшие фрагменты
                if (time % 50 == 0)
                    CleanupStaleFragments();

                lock (RateLock)
                {
                    if (time >= 10)
                    {
                        time = 0;
                        SendByteSpeed = (SendByteSpeed + SendingBytesCount) / 2;
                        SendingBytesCount = 0;
                    }

                    int pendingCount;
                    lock (PendingLock)
                    {
                        pendingCount = PendingReliable.Count;
                    }

                    float rate;
                    if (pendingCount == 0)
                    {
                        rate = 1f;
                    }
                    else
                    {
                        rate = 1f - (float)pendingCount / UDPConstants.MaxPendingReliablePackets;
                        if (rate < 0) rate = 0;
                    }

                    if (rate < DeliveredRate)
                        DeliveredRate = rate;
                    else
                        DeliveredRate = (DeliveredRate * 10 + rate) / 11;
                }
            }
        }

        #endregion

        #region Address

        public string GetRemoteClientAddress()
        {
            try
            {
                return $"{RemoteEndPoint.Address}#{RemoteEndPoint.Port}";
            }
            catch
            {
                return "none";
            }
        }

        #endregion

        #region Helpers

        private async Task<UdpReceiveResult?> ReceiveWithTimeout(CancellationToken token)
        {
            try
            {
                var receiveTask = Socket.ReceiveAsync();
                var delayTask = Task.Delay(-1, token);

                var completed = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);
                if (completed == receiveTask)
                {
                    return receiveTask.Result;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }

    #region Internal Types

    /// <summary>
    /// Ожидающий подтверждения надёжный пакет
    /// </summary>
    internal class PendingPacket
    {
        /// <summary>
        /// Данные дейтаграммы (полный пакет включая заголовок)
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        /// Длина данных
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// Время отправки (Ticks)
        /// </summary>
        public long SentTicks;

        /// <summary>
        /// Кол-во попыток отправки
        /// </summary>
        public int Attempts;

        /// <summary>
        /// Текущий интервал ретрансляции (мс)
        /// </summary>
        public int RetransmitIntervalMs;

        public PendingPacket(byte[] data, int length)
        {
            Data = data;
            Length = length;
            SentTicks = DateTime.UtcNow.Ticks;
            Attempts = 0;
            RetransmitIntervalMs = UDPConstants.InitialRetransmitIntervalMs;
        }
    }

    /// <summary>
    /// Сборщик фрагментов одного сообщения
    /// </summary>
    internal class FragmentAssembly : IDisposable
    {
        private readonly byte[][] Fragments;
        private readonly int[] FragmentLengths;
        private int ReceivedCount;
        public readonly int TotalCount;
        public readonly long CreatedTicks;

        public bool IsComplete => ReceivedCount == TotalCount;

        public FragmentAssembly(int fragmentCount)
        {
            TotalCount = fragmentCount;
            Fragments = new byte[fragmentCount][];
            FragmentLengths = new int[fragmentCount];
            ReceivedCount = 0;
            CreatedTicks = DateTime.UtcNow.Ticks;
        }

        public void AddFragment(ushort index, byte[] data, int offset, int length)
        {
            if (index >= TotalCount)
                return;

            if (Fragments[index] != null)
                return; // Дубликат

            byte[] copy = new byte[length];
            Array.Copy(data, offset, copy, 0, length);
            Fragments[index] = copy;
            FragmentLengths[index] = length;
            ReceivedCount++;
        }

        public INGCArray BuildMessage()
        {
            int totalSize = 0;
            for (int i = 0; i < TotalCount; i++)
                totalSize += FragmentLengths[i];

            var result = new NGCArray(totalSize);
            int writeOffset = 0;
            for (int i = 0; i < TotalCount; i++)
            {
                Array.Copy(Fragments[i], 0, result.Bytes, writeOffset, FragmentLengths[i]);
                writeOffset += FragmentLengths[i];
            }

            return result;
        }

        public void Dispose()
        {
            // Cleanup references
            for (int i = 0; i < Fragments.Length; i++)
                Fragments[i] = null;
        }
    }

    #endregion
}
