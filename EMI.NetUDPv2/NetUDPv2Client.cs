using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EMI.MyException;
using EMI.Network;
using EMI.NGC;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// UDP v2 клиент — production-quality реализация INetworkClient.
    /// 
    /// Ключевые отличия от v1 (EMI.NetUDP):
    /// - Selective retransmission: при потере фрагмента перепосылается только он, а не всё сообщение
    /// - SACK (Selective ACK): 64-битное ackBitfield + SACK-диапазоны для точного трекинга доставки
    /// - NACK: получатель может явно запросить пропущенные пакеты при обнаружении дыры
    /// - Congestion Control (AIMD): окно перегрузки с slow start, congestion avoidance, fast retransmit
    /// - RTT-adaptive RTO: RTO на базе реального RTT (RFC 6298) вместо фиксированного интервала
    /// - Fast retransmit: 3 dup-ACK → немедленная ретрансмиссия без ожидания таймаута
    /// - 32-bit sequence numbers: 4 млрд номеров вместо 65к (устойчивость при высоком трафике)
    /// - Pacing: плавная отправка вместо burst-ов
    /// - Send window: отклонение отправки если окно перегрузки заполнено (backpressure)
    /// </summary>
    public class NetUDPv2Client : INetworkClient
    {
        #region INetworkClient Properties

        public bool IsConnect { get; private set; }
        public int SendByteSpeed { get; private set; }
        public float DeliveredRate => _congestion?.DeliveredRate ?? 1f;
        public RandomDropType RandomDrop { get; set; } = RandomDropType.NoGuaranteed;
        public event INetworkClientDisconnected Disconnected;

        #endregion

        #region MTU Configuration

        private int _mtu = V2Constants.DefaultMTU;

        /// <summary>
        /// Текущий MTU. Устанавливать через SetMTU() ДО Connect().
        /// Для LAN/гигабита рекомендуется 8192-65507.
        /// По умолчанию: 1400 (безопасно для интернета).
        /// </summary>
        public int MTU => _mtu;

        /// <summary>
        /// Устанавливает MTU. Вызывать ДО Connect() и ДО Server.Start().
        /// Обе стороны (клиент и сервер) должны использовать одинаковый MTU.
        /// Для LAN: 8192-65507. Для интернета: 1200-1400.
        /// </summary>
        /// <param name="mtu">Размер пакета в байтах (576..65507)</param>
        public void SetMTU(int mtu)
        {
            if (IsConnect)
                throw new InvalidOperationException("Cannot change MTU while connected");
            _mtu = Math.Clamp(mtu, V2Constants.MinMTU, V2Constants.MaxMTU);
        }

        #endregion

        #region Network

        private UdpClient _socket;
        private IPEndPoint _remoteEndPoint;
        private readonly NetUDPv2Server _server;
        private bool IsServerSide => _server != null;

        #endregion

        #region Reliability State

        /// <summary>Следующий sequence для отправки</summary>
        private uint _sendSequence;

        /// <summary>Последний полученный sequence от удалённой стороны</summary>
        private uint _remoteSequence;

        /// <summary>Получен ли хотя бы один надёжный пакет</summary>
        private bool _hasReceivedReliable;

        /// <summary>64-битное битовое поле полученных пакетов</summary>
        private ulong _ackBitfield;

        /// <summary>Ожидающие подтверждения надёжные пакеты</summary>
        private readonly Dictionary<uint, PendingPacket> _pendingReliable = new Dictionary<uint, PendingPacket>();
        private readonly object _pendingLock = new object();

        /// <summary>SACK-диапазоны для ACK пакетов</summary>
        private SackRange[] _sackRanges = new SackRange[V2Constants.MaxSackRanges];
        private int _sackRangeCount;

        /// <summary>Последовательности, принятые за пределами 64-бит bitfield (для SACK)</summary>
        private readonly HashSet<uint> _receivedOutOfWindow = new HashSet<uint>();

        /// <summary>Набор полученных seq для дедупликации (скользящее окно)</summary>
        private readonly HashSet<uint> _recentlyReceived = new HashSet<uint>();

        /// <summary>Fast retransmit: seq-кандидат на ретрансмит (oldest unacked)</summary>
        private uint _fastRetransmitCandidate;
        /// <summary>Fast retransmit: сколько ACK-раундов подряд этот seq остаётся oldest</summary>
        private int _fastRetransmitCount;
        /// <summary>Fast retransmit: есть ли активный кандидат</summary>
        private bool _hasFastRetransmitCandidate;
        /// <summary>Fast retransmit: cooldown timestamp — не ретрансмитировать до этого момента</summary>
        private long _fastRetransmitCooldownUntilMs;

        /// <summary>Счётчик неподтверждённых reliable пакетов для delayed ACK</summary>
        private int _unackedReliableCount;

        /// <summary>Сигнал для WaitForCwnd: отправитель просыпается когда CWND освободился</summary>
        private SemaphoreSlim _cwndSignal;

        /// <summary>Для последовательной отправки</summary>
        private SemaphoreSlim _sendSemaphore;

        #endregion

        #region Congestion Control

        private CongestionController _congestion;

        #endregion

        #region Fragmentation

        /// <summary>Следующий ID фрагментированного сообщения</summary>
        private uint _nextMessageId;

        /// <summary>Буферы сборки фрагментов</summary>
        private readonly Dictionary<uint, FragmentAssembly> _fragmentBuffers = new Dictionary<uint, FragmentAssembly>();
        private readonly object _fragmentLock = new object();

        #endregion

        #region Receive Queue

        private Channel<INGCArray> _receiveQueue;

        #endregion

        #region Rate Tracking

        private readonly object _rateLock = new object();
        private int _sendingBytesWindow;
        private long _lastRateCalcMs;
        private readonly Random _rnd = new Random();

        #endregion

        #region Pre-allocated Buffers

        private readonly byte[] _ackBuffer = new byte[V2Headers.AckBaseSize + V2Constants.MaxSackRanges * V2Headers.SackRangeSize];
        private readonly byte[] _pingBuffer = new byte[V2Headers.PingSize];
        private byte[] _datagramBuffer = new byte[V2Constants.DefaultMTU];

        /// <summary>
        /// Набор seq для NACK (переиспользуемый)
        /// </summary>
        private readonly uint[] _nackSeqBuffer = new uint[V2Constants.MaxNackSequences];
        private readonly byte[] _nackSendBuffer = new byte[V2Headers.NackBaseSize + V2Constants.MaxNackSequences * 4];
        /// <summary>Seq которые были missing в предыдущей NACK-проверке (для persistence check)</summary>
        private readonly HashSet<uint> _previousNackMissing = new HashSet<uint>();

        /// <summary>
        /// Pre-allocated буфер для сортировки SACK seq (без аллокации List).
        /// Должен вмещать весь _receivedOutOfWindow для полного покрытия SACK.
        /// </summary>
        private readonly uint[] _sackSortBuffer = new uint[8192];

        /// <summary>
        /// Pre-allocated буфер для toRemove в UpdateReceivedSequence
        /// </summary>
        private readonly List<uint> _tempRemoveList = new List<uint>();

        #endregion

        #region Timing

        private long _lastReceivedMs;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        /// <summary>
        /// Флаг: нужно ли послать ACK (устанавливается при приёме reliable пакета)
        /// </summary>
        private volatile bool _ackPending;

        /// <summary>
        /// Последний момент отправки ACK
        /// </summary>
        private long _lastAckSentMs;

        /// <summary>
        /// Timestamp для пересчёта NACK
        /// </summary>
        private long _lastNackCheckMs;

        /// <summary>
        /// Ожидаемый следующий seq для обнаружения дыр (NACK)
        /// </summary>
        private uint _expectedNextSeq;
        private bool _hasExpectedSeq;

        #endregion

        #region Constructors

        /// <summary>
        /// Клиентская инициализация (для исходящего подключения)
        /// </summary>
        public NetUDPv2Client()
        {
            _server = null;
        }

        /// <summary>
        /// Серверная инициализация (сервер создаёт для принятого подключения)
        /// </summary>
        internal NetUDPv2Client(NetUDPv2Server server, IPEndPoint remoteEP, int mtu = 0)
        {
            _server = server;
            _remoteEndPoint = remoteEP;
            if (mtu > 0) _mtu = Math.Clamp(mtu, V2Constants.MinMTU, V2Constants.MaxMTU);
            IsConnect = true;
            InitConnection();
            StartBackgroundLoops();
        }

        #endregion

        #region Connection

        public async Task<bool> Connect(string address, CancellationToken token)
        {
            if (IsServerSide)
                throw new NotSupportedException("Cannot connect from server-side client");

            if (IsConnect)
                throw new AlreadyException("Client is already connected!");

            try
            {
                var ep = V2Utilities.ParseIPAddress(address);
                _socket = new UdpClient();
                _socket.Client.ReceiveBufferSize = V2Constants.SocketBufferSize;
                _socket.Client.SendBufferSize = V2Constants.SocketBufferSize;
                _socket.Connect(ep);
                _remoteEndPoint = ep;

                ulong connectionToken = (ulong)DateTime.UtcNow.Ticks ^ (ulong)_rnd.Next();

                var buffer = new byte[V2Headers.ConnectionRequestSize];
                V2Headers.WriteConnectionRequest(buffer, 0, V2Constants.ProtocolVersion, connectionToken);

                for (int attempt = 0; attempt < V2Constants.MaxConnectAttempts; attempt++)
                {
                    if (token.IsCancellationRequested)
                        break;

                    await _socket.SendAsync(buffer, buffer.Length).ConfigureAwait(false);

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    cts.CancelAfter(V2Constants.ConnectAttemptTimeoutMs);

                    try
                    {
                        var result = await ReceiveWithTimeout(cts.Token).ConfigureAwait(false);
                        if (result.HasValue && result.Value.Buffer.Length >= V2Headers.ConnectionAcceptSize)
                        {
                            var type = (V2PacketType)result.Value.Buffer[0];
                            if (type == V2PacketType.ConnectionAccept)
                            {
                                var acceptToken = V2Headers.ReadConnectionAcceptToken(result.Value.Buffer, 0);
                                if (acceptToken == connectionToken)
                                {
                                    IsConnect = true;
                                    InitConnection();
                                    _ = Task.Run(ClientReceiveLoop);
                                    StartBackgroundLoops();
                                    return true;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                }

                try { _socket?.Close(); } catch { }
                _socket = null;
                return false;
            }
            catch (Exception e)
            {
                try { _socket?.Close(); } catch { }
                _socket = null;
                Disconnected?.Invoke(e.ToString());
                return false;
            }
        }

        private void InitConnection()
        {
            _sendSequence = 0;
            _remoteSequence = 0;
            _hasReceivedReliable = false;
            _ackBitfield = 0;
            _nextMessageId = 0;
            _sackRangeCount = 0;
            _hasFastRetransmitCandidate = false;
            _fastRetransmitCount = 0;
            _fastRetransmitCooldownUntilMs = 0;
            _unackedReliableCount = 0;
            _sendingBytesWindow = 0;
            _lastRateCalcMs = _sw.ElapsedMilliseconds;
            SendByteSpeed = 0;
            _lastReceivedMs = _sw.ElapsedMilliseconds;
            _ackPending = false;
            _lastAckSentMs = 0;
            _lastNackCheckMs = 0;
            _hasExpectedSeq = false;
            _receivedOutOfWindow.Clear();
            _recentlyReceived.Clear();

            // Пере-создаём datagram буфер под текущий MTU
            if (_datagramBuffer.Length < _mtu)
                _datagramBuffer = new byte[_mtu];

            _congestion = new CongestionController();
            _sendSemaphore = new SemaphoreSlim(1, 1);
            _cwndSignal = new SemaphoreSlim(0, 1);
            _receiveQueue = Channel.CreateBounded<INGCArray>(V2Constants.ReceiveQueueCapacity);

            lock (_pendingLock)
                _pendingReliable.Clear();

            lock (_fragmentLock)
            {
                foreach (var fb in _fragmentBuffers.Values)
                    fb.Dispose();
                _fragmentBuffers.Clear();
            }
        }

        private void StartBackgroundLoops()
        {
            _ = Task.Run(RetransmitAndAckLoop);
            _ = Task.Run(PingLoop);
            _ = Task.Run(RateCalcLoop);
        }

        public void Disconnect(string user_error)
        {
            if (!IsConnect)
                return;

            IsConnect = false;

            try
            {
                byte[] reasonBytes = System.Text.Encoding.UTF8.GetBytes(user_error ?? "");
                int len = 1 + reasonBytes.Length;
                byte[] packet = new byte[len];
                packet[0] = (byte)V2PacketType.Disconnect;
                if (reasonBytes.Length > 0)
                    Array.Copy(reasonBytes, 0, packet, 1, reasonBytes.Length);
                SendRaw(packet, len);
            }
            catch { }

            CleanupConnection();
            Disconnected?.Invoke(user_error);
        }

        private void CleanupConnection()
        {
            lock (_pendingLock)
                _pendingReliable.Clear();

            lock (_fragmentLock)
            {
                foreach (var fb in _fragmentBuffers.Values)
                    fb.Dispose();
                _fragmentBuffers.Clear();
            }

            lock (_pendingLock)
            {
                _receivedOutOfWindow.Clear();
                _recentlyReceived.Clear();
            }

            if (!IsServerSide)
            {
                try { _socket?.Close(); } catch { }
            }
            else
            {
                _server?.RemoveClient(this, _remoteEndPoint);
            }

            try { _receiveQueue?.Writer.TryComplete(); } catch { }

            try { _sendSemaphore?.Dispose(); } catch { }
            _sendSemaphore = new SemaphoreSlim(1, 1);

            // Будим WaitForCwnd чтобы он мог завершиться
            if (_cwndSignal != null && _cwndSignal.CurrentCount == 0)
            {
                try { _cwndSignal.Release(); } catch { }
            }
        }

        #endregion

        #region Send

        public async Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            if (!IsConnect)
                return;

            if (!ShouldSend(guaranteed))
                return;

            if (guaranteed)
                await SendReliable(array, token).ConfigureAwait(false);
            else
                await SendUnreliable(array, token).ConfigureAwait(false);
        }

        private async Task SendUnreliable(INGCArray array, CancellationToken token)
        {
            int maxPayload = V2Constants.CalcMaxUnreliablePayload(_mtu);
            if (array.Length <= maxPayload)
            {
                await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    // Если буфер мал для текущего MTU, используем временный
                    byte[] buf = _datagramBuffer.Length >= 1 + array.Length ? _datagramBuffer : new byte[1 + array.Length];
                    buf[0] = (byte)V2PacketType.UnreliableData;
                    Array.Copy(array.Bytes, 0, buf, 1, array.Length);
                    int totalLen = 1 + array.Length;
                    TrackSendBytes(totalLen);
                    SendRaw(buf, totalLen);
                }
                finally { try { _sendSemaphore.Release(); } catch { } }
            }
            else
            {
                await SendFragmented(array, false, token).ConfigureAwait(false);
            }
        }

        private async Task SendReliable(INGCArray array, CancellationToken token)
        {
            int maxPayload = V2Constants.CalcMaxReliablePayload(_mtu);
            if (array.Length <= maxPayload)
            {
                // Ожидаем если окно заполнено (backpressure)
                await WaitForCwnd(token).ConfigureAwait(false);

                await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    uint seq = _sendSequence++;
                    int totalLen = V2Headers.ReliableHeaderSize + array.Length;
                    byte[] packetData = new byte[totalLen];

                    V2Headers.WriteReliableHeader(packetData, 0, V2PacketType.ReliableData, seq);
                    Array.Copy(array.Bytes, 0, packetData, V2Headers.ReliableHeaderSize, array.Length);

                    long nowMs = _sw.ElapsedMilliseconds;
                    var pending = new PendingPacket(packetData, totalLen, seq, nowMs);

                    lock (_pendingLock)
                    {
                        _pendingReliable[seq] = pending;
                    }

                    _congestion.OnPacketSent();
                    TrackSendBytes(totalLen);
                    SendRaw(packetData, totalLen);
                }
                finally { try { _sendSemaphore.Release(); } catch { } }
            }
            else
            {
                await SendFragmented(array, true, token).ConfigureAwait(false);
            }
        }

        private async Task SendFragmented(INGCArray array, bool reliable, CancellationToken token)
        {
            int maxPayload = reliable
                ? V2Constants.CalcMaxReliableFragmentPayload(_mtu)
                : V2Constants.CalcMaxUnreliableFragmentPayload(_mtu);

            int fragmentCount = (array.Length + maxPayload - 1) / maxPayload;
            if (fragmentCount > V2Constants.MaxFragmentCount)
                throw new ClientViolationRightsException(
                    $"Message too large for fragmentation: {array.Length} bytes, max {(long)V2Constants.MaxFragmentCount * maxPayload}");

            uint msgId;
            await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
            try { msgId = _nextMessageId++; }
            finally { try { _sendSemaphore.Release(); } catch { } }

            if (reliable)
            {
                // Батчевая отправка reliable фрагментов: готовим пачку, отправляем за один захват семафора
                int i = 0;
                while (i < fragmentCount)
                {
                    if (token.IsCancellationRequested || !IsConnect)
                        return;

                    // Ждём пока CWND позволит отправить хотя бы один пакет
                    await WaitForCwnd(token).ConfigureAwait(false);

                    await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        // Отправляем столько фрагментов, сколько позволяет CWND
                        while (i < fragmentCount && _congestion.CanSend)
                        {
                            if (token.IsCancellationRequested || !IsConnect)
                                return;

                            int offset = i * maxPayload;
                            int payloadLen = Math.Min(maxPayload, array.Length - offset);

                            uint seq = _sendSequence++;
                            int totalLen = V2Headers.ReliableHeaderSize + V2Headers.FragmentHeaderSize + payloadLen;
                            byte[] packetData = new byte[totalLen];

                            V2Headers.WriteReliableHeader(packetData, 0, V2PacketType.ReliableFragment, seq);
                            V2Headers.WriteFragmentHeader(packetData, V2Headers.ReliableHeaderSize, msgId, (ushort)i, (ushort)fragmentCount);
                            Array.Copy(array.Bytes, offset, packetData,
                                V2Headers.ReliableHeaderSize + V2Headers.FragmentHeaderSize, payloadLen);

                            long nowMs = _sw.ElapsedMilliseconds;
                            var pending = new PendingPacket(packetData, totalLen, seq, nowMs);

                            lock (_pendingLock)
                            {
                                _pendingReliable[seq] = pending;
                            }

                            _congestion.OnPacketSent();
                            TrackSendBytes(totalLen);
                            SendRaw(packetData, totalLen);
                            i++;
                        }
                    }
                    finally { try { _sendSemaphore.Release(); } catch { } }
                }
            }
            else
            {
                // Батчевая отправка unreliable фрагментов
                await _sendSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    for (int i = 0; i < fragmentCount; i++)
                    {
                        if (token.IsCancellationRequested || !IsConnect)
                            return;

                        int offset = i * maxPayload;
                        int payloadLen = Math.Min(maxPayload, array.Length - offset);
                        int totalLen = 1 + V2Headers.FragmentHeaderSize + payloadLen;

                        // Для unreliable нужен отдельный буфер т.к. _datagramBuffer может быть перезаписан
                        byte[] fragBuf = new byte[totalLen];
                        fragBuf[0] = (byte)V2PacketType.UnreliableFragment;
                        V2Headers.WriteFragmentHeader(fragBuf, 1, msgId, (ushort)i, (ushort)fragmentCount);
                        Array.Copy(array.Bytes, offset, fragBuf,
                            1 + V2Headers.FragmentHeaderSize, payloadLen);
                        TrackSendBytes(totalLen);
                        SendRaw(fragBuf, totalLen);
                    }
                }
                finally { try { _sendSemaphore.Release(); } catch { } }
            }
        }

        /// <summary>
        /// Ожидает пока CWND позволит отправить пакет (backpressure).
        /// Использует SemaphoreSlim-сигнал от ProcessAck для мгновенного пробуждения
        /// вместо Task.Delay(1) который на Windows = 15.6мс (timer resolution).
        /// </summary>
        private async Task WaitForCwnd(CancellationToken token)
        {
            int waitLoops = 0;
            while (IsConnect && !token.IsCancellationRequested && _congestion != null && !_congestion.CanSend)
            {
                waitLoops++;
                try
                {
                    await _cwndSignal.WaitAsync(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendRaw(byte[] data, int length)
        {
            try
            {
                if (IsServerSide)
                    _server.SendTo(data, length, _remoteEndPoint);
                else
                    _socket?.Send(data, length);
            }
            catch (Exception e)
            {
                Disconnect(e.Message);
            }
        }

        private void SendAck()
        {
            int len;
            lock (_pendingLock)
            {
                // Строим SACK-диапазоны из _receivedOutOfWindow
                BuildSackRanges();
                len = V2Headers.WriteAck(_ackBuffer, 0, _remoteSequence, _ackBitfield,
                    _sackRanges, _sackRangeCount);
            }
            SendRaw(_ackBuffer, len);
            _lastAckSentMs = _sw.ElapsedMilliseconds;
            _ackPending = false;
            _unackedReliableCount = 0;
        }

        /// <summary>
        /// Строит SACK-диапазоны из _receivedOutOfWindow.
        /// Вызывать под _pendingLock.
        /// </summary>
        private void BuildSackRanges()
        {
            _sackRangeCount = 0;
            if (_receivedOutOfWindow.Count == 0)
                return;

            // Собираем в pre-allocated буфер и сортируем (без аллокации List)
            int count = Math.Min(_receivedOutOfWindow.Count, _sackSortBuffer.Length);
            int idx = 0;
            foreach (uint s in _receivedOutOfWindow)
            {
                _sackSortBuffer[idx++] = s;
                if (idx >= count) break;
            }
            Array.Sort(_sackSortBuffer, 0, count);

            // Формируем непрерывные диапазоны
            uint rangeStart = _sackSortBuffer[0];
            uint rangeEnd = _sackSortBuffer[0];

            for (int i = 1; i < count; i++)
            {
                if (_sackSortBuffer[i] == rangeEnd + 1)
                {
                    rangeEnd = _sackSortBuffer[i];
                }
                else
                {
                    if (_sackRangeCount < V2Constants.MaxSackRanges)
                        _sackRanges[_sackRangeCount++] = new SackRange(rangeStart, rangeEnd);
                    rangeStart = _sackSortBuffer[i];
                    rangeEnd = _sackSortBuffer[i];
                }
            }

            // Последний диапазон
            if (_sackRangeCount < V2Constants.MaxSackRanges)
                _sackRanges[_sackRangeCount++] = new SackRange(rangeStart, rangeEnd);
        }

        #endregion

        #region Receive

        public async Task<INGCArray> AcceptPacket(int max_size, CancellationToken token)
        {
            try
            {
                var result = await _receiveQueue.Reader.ReadAsync(token).ConfigureAwait(false);
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

        private async Task ClientReceiveLoop()
        {
            while (IsConnect)
            {
                try
                {
                    var result = await _socket.ReceiveAsync().ConfigureAwait(false);
                    if (result.Buffer != null && result.Buffer.Length > 0)
                        ProcessIncomingDatagram(result.Buffer, result.Buffer.Length);
                }
                catch (Exception)
                {
                    if (IsConnect) Disconnect("Receive error");
                    break;
                }
            }
        }

        /// <summary>
        /// Обрабатывает входящую дейтаграмму
        /// </summary>
        internal void ProcessIncomingDatagram(byte[] data, int length)
        {
            if (length < 1 || !IsConnect)
                return;

            _lastReceivedMs = _sw.ElapsedMilliseconds;

            var type = (V2PacketType)data[0];

            switch (type)
            {
                case V2PacketType.UnreliableData:
                    HandleUnreliableData(data, length);
                    break;
                case V2PacketType.ReliableData:
                    HandleReliableData(data, length);
                    break;
                case V2PacketType.UnreliableFragment:
                    HandleUnreliableFragment(data, length);
                    break;
                case V2PacketType.ReliableFragment:
                    HandleReliableFragment(data, length);
                    break;
                case V2PacketType.Ack:
                    HandleAck(data, length);
                    break;
                case V2PacketType.Disconnect:
                    HandleDisconnect(data, length);
                    break;
                case V2PacketType.Ping:
                    HandlePing(data, length);
                    break;
                case V2PacketType.Pong:
                    HandlePong(data, length);
                    break;
                case V2PacketType.Nack:
                    HandleNack(data, length);
                    break;
            }
        }

        private void HandleUnreliableData(byte[] data, int length)
        {
            int payloadLen = length - 1;
            if (payloadLen <= 0) return;

            var ngcArray = new NGCArray(payloadLen);
            Array.Copy(data, 1, ngcArray.Bytes, 0, payloadLen);

            if (!_receiveQueue.Writer.TryWrite(ngcArray))
                ngcArray.Dispose();
        }

        private void HandleReliableData(byte[] data, int length)
        {
            if (length < V2Headers.ReliableHeaderSize) return;

            V2Headers.ReadReliableHeader(data, 0, out _, out uint seq);
            int payloadLen = length - V2Headers.ReliableHeaderSize;

            bool isDuplicate;
            lock (_pendingLock) // Защищаем receiver state: _recentlyReceived, _receivedOutOfWindow, _ackBitfield
            {
                isDuplicate = !_recentlyReceived.Add(seq);
                UpdateReceivedSequence(seq);
                _ackPending = true;
                if (++_unackedReliableCount >= V2Constants.DelayedAckThreshold)
                    SendAck(); // lock(_pendingLock) реентрантен — безопасно
            }

            if (isDuplicate || payloadLen <= 0) return;

            var ngcArray = new NGCArray(payloadLen);
            Array.Copy(data, V2Headers.ReliableHeaderSize, ngcArray.Bytes, 0, payloadLen);

            if (!_receiveQueue.Writer.TryWrite(ngcArray))
                ngcArray.Dispose();
        }

        private void HandleUnreliableFragment(byte[] data, int length)
        {
            if (length < 1 + V2Headers.FragmentHeaderSize) return;

            V2Headers.ReadFragmentHeader(data, 1, out uint msgId, out ushort fragIdx, out ushort fragCount);
            int payloadOffset = 1 + V2Headers.FragmentHeaderSize;
            int payloadLen = length - payloadOffset;
            if (payloadLen <= 0) return;

            AssembleFragment(msgId, fragIdx, fragCount, data, payloadOffset, payloadLen);
        }

        private void HandleReliableFragment(byte[] data, int length)
        {
            if (length < V2Headers.ReliableHeaderSize + V2Headers.FragmentHeaderSize)
                return;

            V2Headers.ReadReliableHeader(data, 0, out _, out uint seq);

            bool isDuplicate;
            lock (_pendingLock) // Защищаем receiver state: _recentlyReceived, _receivedOutOfWindow, _ackBitfield
            {
                isDuplicate = !_recentlyReceived.Add(seq);
                UpdateReceivedSequence(seq);
                _ackPending = true;
                if (++_unackedReliableCount >= V2Constants.DelayedAckThreshold)
                    SendAck(); // lock(_pendingLock) реентрантен — безопасно
            }

            if (isDuplicate) return;

            V2Headers.ReadFragmentHeader(data, V2Headers.ReliableHeaderSize,
                out uint msgId, out ushort fragIdx, out ushort fragCount);
            int payloadOffset = V2Headers.ReliableHeaderSize + V2Headers.FragmentHeaderSize;
            int payloadLen = length - payloadOffset;
            if (payloadLen <= 0) return;

            AssembleFragment(msgId, fragIdx, fragCount, data, payloadOffset, payloadLen);
        }

        private void AssembleFragment(uint msgId, ushort fragIdx, ushort fragCount,
            byte[] data, int payloadOffset, int payloadLen)
        {
            lock (_fragmentLock)
            {
                if (!_fragmentBuffers.TryGetValue(msgId, out var assembly))
                {
                    assembly = new FragmentAssembly(fragCount, _sw.ElapsedMilliseconds);
                    _fragmentBuffers[msgId] = assembly;
                }

                assembly.AddFragment(fragIdx, data, payloadOffset, payloadLen);

                if (assembly.IsComplete)
                {
                    var assembled = assembly.BuildMessage();
                    _fragmentBuffers.Remove(msgId);

                    if (!_receiveQueue.Writer.TryWrite(assembled))
                        assembled.Dispose();
                }
            }
        }

        private void HandleAck(byte[] data, int length)
        {
            if (length < V2Headers.AckBaseSize) return;

            V2Headers.ReadAck(data, 0, length, out uint ackSeq, out ulong ackBits, out SackRange[] sackRanges);
            ProcessAck(ackSeq, ackBits, sackRanges);
        }

        private void HandleDisconnect(byte[] data, int length)
        {
            string reason = "Remote disconnect";
            if (length > 1)
            {
                try { reason = System.Text.Encoding.UTF8.GetString(data, 1, length - 1); }
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
            if (length < V2Headers.PingSize) return;

            V2Headers.ReadPing(data, 0, out ushort pingId, out long timestamp);
            V2Headers.WritePong(_pingBuffer, 0, pingId, timestamp);
            SendRaw(_pingBuffer, V2Headers.PingSize);
        }

        private void HandlePong(byte[] data, int length)
        {
            if (length < V2Headers.PingSize) return;

            V2Headers.ReadPing(data, 0, out _, out long echoTimestamp);
            long nowMs = _sw.ElapsedMilliseconds;
            double rttMs = nowMs - echoTimestamp;
            if (rttMs >= 0 && rttMs < 30000)
            {
                _congestion?.UpdateRtt(rttMs);
            }
        }

        private void HandleNack(byte[] data, int length)
        {
            if (length < V2Headers.NackBaseSize) return;

            V2Headers.ReadNack(data, 0, length, out uint[] sequences);

            // Ретрансмитим запрошенные пакеты
            lock (_pendingLock)
            {
                foreach (uint seq in sequences)
                {
                    if (_pendingReliable.TryGetValue(seq, out var pending))
                    {
                        pending.SentMs = _sw.ElapsedMilliseconds;
                        _congestion?.OnRetransmit();
                        SendRaw(pending.Data, pending.Length);
                    }
                }
            }
        }

        #endregion

        #region Reliability Layer

        /// <summary>
        /// Обновляет ACK-состояние при получении нового sequence.
        /// Также поддерживает SACK-набор для пакетов, выходящих за пределы 64-бит bitfield.
        /// </summary>
        private void UpdateReceivedSequence(uint sequence)
        {
            if (!_hasReceivedReliable)
            {
                _hasReceivedReliable = true;
                _remoteSequence = sequence;
                _ackBitfield = 0;
                _expectedNextSeq = sequence + 1;
                _hasExpectedSeq = true;
                return;
            }

            long diff = SequenceDiff(sequence, _remoteSequence);

            if (diff > 0)
            {
                // Перед сдвигом: биты, которые уплывут за пределы 64 — перенести в SACK
                if (diff < V2Constants.AckBitfieldSize)
                {
                    // Биты, которые сдвинутся за пределы 64, сохраняем в SACK
                    for (int i = V2Constants.AckBitfieldSize - (int)diff; i < V2Constants.AckBitfieldSize; i++)
                    {
                        if ((_ackBitfield & (1UL << i)) != 0)
                        {
                            uint oldSeq = _remoteSequence - (uint)(1 + i);
                            _receivedOutOfWindow.Add(oldSeq);
                        }
                    }
                    _ackBitfield = (_ackBitfield << (int)diff) | (1UL << ((int)diff - 1));
                }
                else
                {
                    // Все 64 бита уплыли — сохраняем все установленные в SACK
                    for (int i = 0; i < V2Constants.AckBitfieldSize; i++)
                    {
                        if ((_ackBitfield & (1UL << i)) != 0)
                        {
                            uint oldSeq = _remoteSequence - (uint)(1 + i);
                            _receivedOutOfWindow.Add(oldSeq);
                        }
                    }
                    // Также сам _remoteSequence был подтверждён (он = ackSeq)
                    _receivedOutOfWindow.Add(_remoteSequence);
                    _ackBitfield = 0;
                }

                _remoteSequence = sequence;

                // Проверяем, есть ли в _receivedOutOfWindow записи, которые теперь попали в окно bitfield
                _tempRemoveList.Clear();
                foreach (uint s in _receivedOutOfWindow)
                {
                    long d = SequenceDiff(s, _remoteSequence);
                    if (d < 0)
                    {
                        int bitIdx = (int)(-d) - 1;
                        if (bitIdx < V2Constants.AckBitfieldSize)
                        {
                            _ackBitfield |= (1UL << bitIdx);
                            _tempRemoveList.Add(s);
                        }
                    }
                    else
                    {
                        // s >= _remoteSequence — не должно быть, или это дубликат _remoteSequence
                        _tempRemoveList.Add(s);
                    }
                }
                foreach (uint s in _tempRemoveList)
                    _receivedOutOfWindow.Remove(s);
            }
            else if (diff < 0)
            {
                int bitIndex = (int)(-diff) - 1;
                if (bitIndex < V2Constants.AckBitfieldSize)
                    _ackBitfield |= (1UL << bitIndex);
                else
                    _receivedOutOfWindow.Add(sequence); // За пределами bitfield — в SACK
            }

            // Ограничиваем размер SACK-набора: убираем записи слишком далеко от _remoteSequence.
            // НЕ Clear() — иначе потеря всех SACK-диапазонов, и ACK не подтвердит пакеты
            // за пределами 64-бит bitfield → вечный stall InFlight > Cwnd.
            if (_receivedOutOfWindow.Count > 1024)
            {
                _tempRemoveList.Clear();
                foreach (uint s in _receivedOutOfWindow)
                {
                    long d = SequenceDiff(s, _remoteSequence);
                    if (d < -8192) // За пределами разумного окна — удаляем
                        _tempRemoveList.Add(s);
                }
                foreach (uint s in _tempRemoveList)
                    _receivedOutOfWindow.Remove(s);
            }

            // Ограничиваем размер _recentlyReceived: убираем записи далеко от _remoteSequence
            if (_recentlyReceived.Count > 8192)
            {
                _tempRemoveList.Clear();
                foreach (uint s in _recentlyReceived)
                {
                    long d = SequenceDiff(s, _remoteSequence);
                    if (d < -16384)
                        _tempRemoveList.Add(s);
                }
                foreach (uint s in _tempRemoveList)
                    _recentlyReceived.Remove(s);
            }

            // Трекинг для NACK
            if (_hasExpectedSeq)
            {
                _expectedNextSeq = _remoteSequence + 1;
            }
        }

        /// <summary>
        /// Обрабатывает ACK (с SACK).
        /// Включает gap-based fast retransmit: если в ACK видны подтверждённые seq
        /// выше неподтверждённого — считаем его потерянным.
        /// </summary>
        private void ProcessAck(uint ackSequence, ulong ackBits, SackRange[] sackRanges)
        {
            long nowMs = _sw.ElapsedMilliseconds;

            lock (_pendingLock)
            {
                int ackedCount = 0;

                // Подтверждаем основной sequence
                if (_pendingReliable.TryGetValue(ackSequence, out var mainPending))
                {
                    double rttMs = nowMs - mainPending.FirstSentMs;
                    if (mainPending.Attempts == 0) // RTT достоверен только для первой попытки
                        _congestion?.OnPacketAcked(rttMs);
                    else
                        _congestion?.OnPacketAcked(-1);

                    _pendingReliable.Remove(ackSequence);
                    ackedCount++;
                }

                // Подтверждаем из битового поля
                for (int i = 0; i < V2Constants.AckBitfieldSize; i++)
                {
                    if ((ackBits & (1UL << i)) != 0)
                    {
                        uint seq = ackSequence - (uint)(1 + i);
                        if (_pendingReliable.TryGetValue(seq, out var pending))
                        {
                            if (pending.Attempts == 0)
                            {
                                double rttMs = nowMs - pending.FirstSentMs;
                                _congestion?.OnPacketAcked(rttMs);
                            }
                            else
                            {
                                _congestion?.OnPacketAcked(-1);
                            }
                            _pendingReliable.Remove(seq);
                            ackedCount++;
                        }
                    }
                }

                // Подтверждаем из SACK-диапазонов
                if (sackRanges != null)
                {
                    foreach (var range in sackRanges)
                    {
                        for (uint s = range.Start; s <= range.End; s++)
                        {
                            if (_pendingReliable.TryGetValue(s, out var p))
                            {
                                _congestion?.OnPacketAcked(p.Attempts == 0 ? (nowMs - p.FirstSentMs) : -1);
                                _pendingReliable.Remove(s);
                                ackedCount++;
                            }
                            // Safety: avoid infinite loop on wrap
                            if (s == uint.MaxValue) break;
                        }
                    }
                }

                // Gap-based fast retransmit:
                // Отслеживаем конкретный seq (oldest unacked). Если один и тот же seq
                // остаётся oldest через N ACK-раундов — значит он реально потерян.
                // Это устраняет ложные срабатывания: при пайплайне всегда есть pending,
                // но если новые ACK приходят и oldest НЕ меняется — это потеря.
                // Cooldown: после ретрансмита ждём RTO, давая пакету время быть ACK'd.
                if (ackedCount > 0 && _pendingReliable.Count > 0 && nowMs >= _fastRetransmitCooldownUntilMs)
                {
                    // Находим oldest unacked
                    uint? oldest = null;
                    long oldestTime = long.MaxValue;
                    foreach (var kv in _pendingReliable)
                    {
                        if (kv.Value.FirstSentMs < oldestTime)
                        {
                            oldestTime = kv.Value.FirstSentMs;
                            oldest = kv.Key;
                        }
                    }

                    if (oldest.HasValue)
                    {
                        if (_hasFastRetransmitCandidate && _fastRetransmitCandidate == oldest.Value)
                        {
                            // Тот же seq всё ещё oldest — инкрементируем
                            _fastRetransmitCount++;
                        }
                        else
                        {
                            // Новый oldest — сбрасываем счётчик
                            _fastRetransmitCandidate = oldest.Value;
                            _fastRetransmitCount = 1;
                            _hasFastRetransmitCandidate = true;
                        }

                        if (_fastRetransmitCount >= V2Constants.FastRetransmitDupAckThreshold
                            && _pendingReliable.TryGetValue(oldest.Value, out var retransmit))
                        {
                            _fastRetransmitCount = 0;
                            _hasFastRetransmitCandidate = false;
                            int rto = _congestion?.Rto ?? V2Constants.InitialRtoMs;
                            _fastRetransmitCooldownUntilMs = nowMs + rto;
                            bool wasFirstAttempt = retransmit.Attempts == 0;
                            retransmit.Attempts++;
                            retransmit.SentMs = nowMs;
                            if (wasFirstAttempt)
                                _congestion?.OnPacketLost();
                            _congestion?.OnRetransmit();
                            SendRaw(retransmit.Data, retransmit.Length);
                        }
                    }
                }
                else if (_pendingReliable.Count == 0)
                {
                    _hasFastRetransmitCandidate = false;
                    _fastRetransmitCount = 0;
                }
            }

            // Сигнализируем WaitForCwnd что CWND освободился
            if (_congestion != null && _congestion.CanSend && _cwndSignal != null && _cwndSignal.CurrentCount == 0)
            {
                try { _cwndSignal.Release(); } catch { }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long SequenceDiff(uint s1, uint s2)
        {
            long diff = (long)s1 - (long)s2;
            // С 32-bit seq переполнение маловероятно, но обрабатываем
            if (diff > int.MaxValue) diff -= (long)uint.MaxValue + 1;
            else if (diff < int.MinValue) diff += (long)uint.MaxValue + 1;
            return diff;
        }

        #endregion

        #region Background Loops

        /// <summary>
        /// Объединённый цикл ретрансмиссии и ACK-отправки.
        /// Тик: каждые RetransmitTickIntervalMs мс.
        /// </summary>
        private async Task RetransmitAndAckLoop()
        {
            while (IsConnect)
            {
                try
                {
                await Task.Delay(V2Constants.RetransmitTickIntervalMs).ConfigureAwait(false);
                if (!IsConnect) break;

                long nowMs = _sw.ElapsedMilliseconds;

                // 1. Отправляем накопленные ACK
                if (_ackPending && (nowMs - _lastAckSentMs >= V2Constants.AckFlushIntervalMs))
                {
                    SendAck();
                }

                // Синхронизируем InFlight с реальным pending count каждый тик.
                // Предотвращает дрейф InFlight >> Cwnd при отсутствии ACK:
                // без этого InFlight может навечно превышать Cwnd → CanSend=false.
                if (_congestion != null)
                {
                    int pendingCount;
                    lock (_pendingLock)
                    {
                        pendingCount = _pendingReliable.Count;
                    }
                    _congestion.SyncInFlight(pendingCount);
                }

                // 2. Ретрансмиссия по таймауту
                List<PendingPacket> toRetransmit = null;
                bool shouldDisconnect = false;

                lock (_pendingLock)
                {
                    int rto = _congestion?.Rto ?? V2Constants.InitialRtoMs;
                    bool firstTimeLossThisTick = false;
                    int retransmitCount = 0;

                    // Лимит burst: min(Cwnd/4, 16) — предотвращает перегрузку
                    // получателя ретрансмитами. При большом Cwnd разрешаем
                    // до 16 за тик; при маленьком — пропорционально Cwnd.
                    int maxRetransmit = Math.Min(Math.Max((_congestion?.Cwnd ?? 64) / 4, 2), 16);

                    foreach (var kv in _pendingReliable)
                    {
                        var pending = kv.Value;
                        long elapsed = nowMs - pending.SentMs;
                        long totalAge = nowMs - pending.FirstSentMs;

                        // Используем RTO с экспоненциальным backoff на уровне пакета
                        int effectiveRto = rto * (1 << Math.Min(pending.Attempts, 5));
                        effectiveRto = Math.Min(effectiveRto, V2Constants.MaxRtoMs);

                        if (elapsed >= effectiveRto)
                        {
                            // Disconnect по количеству попыток ИЛИ по общему времени жизни пакета
                            if (pending.Attempts >= V2Constants.MaxRetransmitAttempts
                                || totalAge >= V2Constants.ReliablePacketLifetimeMs)
                            {
                                shouldDisconnect = true;
                                break;
                            }

                            // Только первый таймаут пакета (Attempts 0→1)
                            // считается новой потерей для CC. Re-retransmit
                            // уже учтённых пакетов НЕ снижает Cwnd — иначе
                            // Cwnd коллапсирует до MinCwnd за секунды.
                            if (pending.Attempts == 0)
                                firstTimeLossThisTick = true;

                            pending.Attempts++;
                            pending.SentMs = nowMs;

                            if (toRetransmit == null) toRetransmit = new List<PendingPacket>();
                            toRetransmit.Add(pending);
                            retransmitCount++;

                            if (retransmitCount >= maxRetransmit) break;
                        }
                    }

                    // OnPacketLost — ОДИН раз за тик, ТОЛЬКО при первом таймауте.
                    // Re-retransmit не вызывает OnPacketLost: предотвращает death spiral
                    // где одни и те же 416 пакетов снижают Cwnd каждые 200мс.
                    if (firstTimeLossThisTick)
                        _congestion?.OnPacketLost();
                }

                if (shouldDisconnect)
                {
                    _ = Task.Run(() => Disconnect("Reliable packet delivery timeout"));
                    return;
                }

                if (toRetransmit != null)
                {
                    foreach (var pending in toRetransmit)
                    {
                        SendRaw(pending.Data, pending.Length);
                    }
                }

                // 3. NACK: проверяем дыры в полученных seq — явно запрашиваем пропущенные пакеты
                if (nowMs - _lastNackCheckMs >= V2Constants.NackDelayMs)
                {
                    _lastNackCheckMs = nowMs;
                    SendNackForGaps();
                }

                // 4. Очистка устаревших фрагментов
                CleanupStaleFragments(nowMs);

                } // try
                catch
                {
                }
            }
        }

        private async Task PingLoop()
        {
            ushort pingId = 0;
            while (IsConnect)
            {
                await Task.Delay(V2Constants.PingIntervalMs).ConfigureAwait(false);
                if (!IsConnect) break;

                long nowMs = _sw.ElapsedMilliseconds;

                // Проверяем таймаут
                if (nowMs - _lastReceivedMs > V2Constants.ConnectionTimeoutMs)
                {
                    Disconnect("Connection timeout");
                    return;
                }

                V2Headers.WritePing(_pingBuffer, 0, pingId++, nowMs);
                SendRaw(_pingBuffer, V2Headers.PingSize);
            }
        }

        private async Task RateCalcLoop()
        {
            while (IsConnect)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (!IsConnect) break;

                lock (_rateLock)
                {
                    long nowMs = _sw.ElapsedMilliseconds;
                    long elapsed = nowMs - _lastRateCalcMs;
                    if (elapsed > 0)
                    {
                        SendByteSpeed = (int)((long)_sendingBytesWindow * 1000 / elapsed);
                        SendByteSpeed = (SendByteSpeed + (int)((long)_sendingBytesWindow * 1000 / elapsed)) / 2;
                    }
                    _sendingBytesWindow = 0;
                    _lastRateCalcMs = nowMs;
                }

                _congestion?.ForceRecalcDeliveredRate();
            }
        }

        #endregion

        #region Fragment Assembly

        private void CleanupStaleFragments(long nowMs)
        {
            lock (_fragmentLock)
            {
                List<uint> toRemove = null;
                foreach (var kv in _fragmentBuffers)
                {
                    if (nowMs - kv.Value.CreatedMs > V2Constants.FragmentTimeoutMs)
                    {
                        if (toRemove == null) toRemove = new List<uint>();
                        toRemove.Add(kv.Key);
                    }
                }

                if (toRemove != null)
                {
                    foreach (var key in toRemove)
                    {
                        if (_fragmentBuffers.TryGetValue(key, out var fb))
                        {
                            fb.Dispose();
                            _fragmentBuffers.Remove(key);
                        }
                    }
                }
            }
        }

        #endregion

        #region NACK

        /// <summary>
        /// Обнаруживает дыры в принятых sequence и отправляет NACK для пропущенных пакетов.
        /// Отправляет NACK только для seq, пропущенных в 2+ последовательных проверках,
        /// чтобы отличить реальную потерю от переупорядочивания пакетов (packet reordering).
        /// </summary>
        private void SendNackForGaps()
        {
            if (!_hasReceivedReliable) return;

            int nackCount = 0;
            // Собираем текущие missing seq
            HashSet<uint> currentMissing = null;

            lock (_pendingLock) // Защищаем receiver state: _ackBitfield, _remoteSequence, _receivedOutOfWindow
            {
                int nackScanWindow = Math.Min(16, V2Constants.AckBitfieldSize);
                for (int i = 0; i < nackScanWindow; i++)
                {
                    if ((_ackBitfield & (1UL << i)) == 0)
                    {
                        uint missingSeq = _remoteSequence - (uint)(1 + i);
                        if (!_receivedOutOfWindow.Contains(missingSeq))
                        {
                            if (currentMissing == null) currentMissing = new HashSet<uint>();
                            currentMissing.Add(missingSeq);

                            // NACK только если был missing и в предыдущей проверке (persistence)
                            if (nackCount < V2Constants.MaxNackSequences && _previousNackMissing.Contains(missingSeq))
                            {
                                _nackSeqBuffer[nackCount++] = missingSeq;
                            }
                        }
                    }
                }

                // Обновляем предыдущий набор
                _previousNackMissing.Clear();
                if (currentMissing != null)
                {
                    foreach (var seq in currentMissing)
                        _previousNackMissing.Add(seq);
                }
            }

            if (nackCount > 0)
            {
                int len = V2Headers.WriteNack(_nackSendBuffer, 0, _nackSeqBuffer, nackCount);
                SendRaw(_nackSendBuffer, len);
            }
        }

        #endregion

        #region Rate Control / Drop

        private bool ShouldSend(bool guaranteed)
        {
            if (RandomDrop == RandomDropType.Nothing)
                return true;

            float rate = DeliveredRate;
            if (rate > 0.99f)
                return true;

            if (RandomDrop == RandomDropType.NoGuaranteed)
            {
                if (guaranteed)
                    return true;
                return _rnd.NextDouble() - (1.0000001 - rate) >= 0;
            }

            // RandomDropType.All
            if (guaranteed) return true;
            return _rnd.NextDouble() - (1.0000001 - rate) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrackSendBytes(int size)
        {
            lock (_rateLock)
            {
                _sendingBytesWindow += size;
            }
        }

        #endregion

        #region Address

        public string GetRemoteClientAddress()
        {
            try
            {
                return $"{_remoteEndPoint.Address}#{_remoteEndPoint.Port}";
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
                var receiveTask = _socket.ReceiveAsync();
                var delayTask = Task.Delay(-1, token);

                var completed = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);
                if (completed == receiveTask)
                    return receiveTask.Result;
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
        public readonly byte[] Data;
        public readonly int Length;
        public readonly uint Sequence;

        /// <summary>
        /// Время первой отправки (для RTT-измерения)
        /// </summary>
        public readonly long FirstSentMs;

        /// <summary>
        /// Время последней отправки (для RTO)
        /// </summary>
        public long SentMs;

        /// <summary>
        /// Число ретрансмиссий
        /// </summary>
        public int Attempts;

        public PendingPacket(byte[] data, int length, uint seq, long sentMs)
        {
            Data = data;
            Length = length;
            Sequence = seq;
            FirstSentMs = sentMs;
            SentMs = sentMs;
            Attempts = 0;
        }
    }

    /// <summary>
    /// Сборщик фрагментов одного сообщения (с отслеживанием доставленных фрагментов)
    /// </summary>
    internal class FragmentAssembly : IDisposable
    {
        private readonly byte[][] _fragments;
        private readonly int[] _fragmentLengths;
        private int _receivedCount;
        public readonly int TotalCount;
        public readonly long CreatedMs;

        public bool IsComplete => _receivedCount == TotalCount;

        public FragmentAssembly(int fragmentCount, long createdMs)
        {
            TotalCount = fragmentCount;
            _fragments = new byte[fragmentCount][];
            _fragmentLengths = new int[fragmentCount];
            _receivedCount = 0;
            CreatedMs = createdMs;
        }

        public void AddFragment(ushort index, byte[] data, int offset, int length)
        {
            if (index >= TotalCount) return;
            if (_fragments[index] != null) return; // дубликат

            byte[] copy = new byte[length];
            Array.Copy(data, offset, copy, 0, length);
            _fragments[index] = copy;
            _fragmentLengths[index] = length;
            _receivedCount++;
        }

        public INGCArray BuildMessage()
        {
            int totalSize = 0;
            for (int i = 0; i < TotalCount; i++)
                totalSize += _fragmentLengths[i];

            var result = new NGCArray(totalSize);
            int writeOffset = 0;
            for (int i = 0; i < TotalCount; i++)
            {
                Array.Copy(_fragments[i], 0, result.Bytes, writeOffset, _fragmentLengths[i]);
                writeOffset += _fragmentLengths[i];
            }
            return result;
        }

        public void Dispose()
        {
            for (int i = 0; i < _fragments.Length; i++)
                _fragments[i] = null;
        }
    }

    #endregion
}
