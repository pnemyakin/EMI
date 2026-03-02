using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EMI.MyException;
using EMI.Network;

namespace EMI.NetUDP
{
    /// <summary>
    /// UDP сервер — принимает подключения и маршрутизирует входящие дейтаграммы по клиентам
    /// </summary>
    public class NetUDPServer : INetworkServer
    {
        private UdpClient Socket;
        private CancellationTokenSource CTS;

        /// <summary>
        /// Подключённые клиенты по адресу
        /// </summary>
        internal readonly Dictionary<EndPointKey, NetUDPClient> Clients = new Dictionary<EndPointKey, NetUDPClient>();
        private readonly object ClientsLock = new object();

        /// <summary>
        /// Ожидающие подтверждения подключения (token → endpoint)
        /// </summary>
        private readonly Dictionary<ulong, IPEndPoint> PendingConnections = new Dictionary<ulong, IPEndPoint>();

        /// <summary>
        /// Очередь новых подключений
        /// </summary>
        private readonly Channel<NetUDPClient> AcceptedClients = Channel.CreateUnbounded<NetUDPClient>();

        /// <summary>
        /// Буфер для отправки пакетов подключения
        /// </summary>
        private readonly byte[] SendBuffer = new byte[UDPConnectionPacket.SizeOf];

        public void StartServer(string address)
        {
            lock (this)
            {
                if (Socket != null)
                    throw new AlreadyException();

                var ep = Utilities.ParseIPAddress(address);
                Socket = new UdpClient(ep);
                CTS = new CancellationTokenSource();

                _ = Task.Factory.StartNew(ReceiveLoop, TaskCreationOptions.LongRunning);
            }
        }

        public void StopServer()
        {
            lock (this)
            {
                CTS?.Cancel();

                lock (ClientsLock)
                {
                    foreach (var client in Clients.Values)
                    {
                        try { client.Disconnect("Server stopped"); }
                        catch { }
                    }
                    Clients.Clear();
                }

                try { Socket?.Close(); }
                catch { }
                Socket = null;
            }
        }

        public async Task<INetworkClient> AcceptClient(CancellationToken token)
        {
            try
            {
                return await AcceptedClients.Reader.ReadAsync(token).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Отправить дейтаграмму через серверный сокет
        /// </summary>
        internal void SendTo(byte[] data, int length, IPEndPoint endPoint)
        {
            try
            {
                Socket?.Send(data, length, endPoint);
            }
            catch { }
        }

        /// <summary>
        /// Асинхронная отправка дейтаграммы
        /// </summary>
        internal async Task SendToAsync(byte[] data, int length, IPEndPoint endPoint)
        {
            try
            {
                if (Socket != null)
                    await Socket.SendAsync(data, length, endPoint).ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>
        /// Удалить клиента из списка
        /// </summary>
        internal void RemoveClient(NetUDPClient client, IPEndPoint endPoint)
        {
            var key = new EndPointKey(endPoint);
            lock (ClientsLock)
            {
                if (Clients.TryGetValue(key, out var existing) && existing == client)
                {
                    Clients.Remove(key);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            while (!CTS.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await Socket.ReceiveAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                if (result.Buffer == null || result.Buffer.Length < 1)
                    continue;

                var type = (UDPPacketType)result.Buffer[0];
                var key = new EndPointKey(result.RemoteEndPoint);

                if (type == UDPPacketType.ConnectionRequest)
                {
                    HandleConnectionRequest(result.RemoteEndPoint, result.Buffer, key);
                }
                else
                {
                    NetUDPClient client = null;
                    lock (ClientsLock)
                    {
                        Clients.TryGetValue(key, out client);
                    }

                    if (client != null)
                    {
                        client.ProcessIncomingDatagram(result.Buffer, result.Buffer.Length);
                    }
                    // Игнорируем пакеты от неизвестных клиентов (защита от спуфинга)
                }
            }
        }

        private void HandleConnectionRequest(IPEndPoint remoteEP, byte[] data, EndPointKey key)
        {
            if (data.Length < UDPConnectionPacket.SizeOf)
                return;

            var connPacket = UDPConnectionPacket.FromBytes(data);

            // Проверяем, не подключён ли уже этот клиент
            lock (ClientsLock)
            {
                if (Clients.ContainsKey(key))
                    return; // уже подключён — игнорируем повторный запрос
            }

            // Создаём серверный клиент
            var client = new NetUDPClient(this, remoteEP);

            lock (ClientsLock)
            {
                Clients[key] = client;
            }

            // Отправляем ConnectionAccept
            var accept = new UDPConnectionPacket(UDPPacketType.ConnectionAccept, connPacket.Token);
            var buffer = new byte[UDPConnectionPacket.SizeOf];
            accept.WriteToBuffer(buffer);
            SendTo(buffer, buffer.Length, remoteEP);

            // Добавляем в очередь принятых клиентов
            AcceptedClients.Writer.TryWrite(client);
        }
    }

    /// <summary>
    /// Struct-ключ для словаря клиентов по IPEndPoint (без аллокаций при поиске)
    /// </summary>
    internal readonly struct EndPointKey : IEquatable<EndPointKey>
    {
        private readonly long AddrLow;
        private readonly long AddrHigh;
        private readonly int Port;

        public EndPointKey(IPEndPoint ep)
        {
            Port = ep.Port;
            byte[] bytes = ep.Address.GetAddressBytes();
            AddrLow = 0;
            AddrHigh = 0;

            if (bytes.Length <= 8)
            {
                for (int i = 0; i < bytes.Length; i++)
                    AddrLow |= ((long)bytes[i]) << (i * 8);
            }
            else
            {
                for (int i = 0; i < 8 && i < bytes.Length; i++)
                    AddrLow |= ((long)bytes[i]) << (i * 8);
                for (int i = 8; i < bytes.Length; i++)
                    AddrHigh |= ((long)bytes[i]) << ((i - 8) * 8);
            }
        }

        public bool Equals(EndPointKey other) =>
            AddrLow == other.AddrLow && AddrHigh == other.AddrHigh && Port == other.Port;

        public override bool Equals(object obj) => obj is EndPointKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = AddrLow.GetHashCode();
                hash = hash * 397 ^ AddrHigh.GetHashCode();
                hash = hash * 397 ^ Port;
                return hash;
            }
        }
    }
}
