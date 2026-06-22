using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EMI.MyException;
using EMI.Network;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// UDP v2 сервер — принимает подключения и маршрутизирует входящие дейтаграммы по клиентам.
    /// Единый серверный сокет для всех клиентов (multiplexing by remote endpoint).
    /// </summary>
    public class NetUDPv2Server : INetworkServer
    {
        private UdpClient _socket;
        private CancellationTokenSource _cts;

        /// <summary>
        /// MTU для всех клиентов этого сервера.
        /// Устанавливать ДО StartServer(). Обе стороны должны использовать одинаковый MTU.
        /// </summary>
        public int MTU { get; set; } = V2Constants.DefaultMTU;

        /// <summary>
        /// Подключённые клиенты по адресу
        /// </summary>
        internal readonly Dictionary<EndPointKey, NetUDPv2Client> Clients = new Dictionary<EndPointKey, NetUDPv2Client>();
        private readonly object _clientsLock = new object();

        /// <summary>
        /// Очередь новых подключений
        /// </summary>
        private readonly Channel<NetUDPv2Client> _acceptedClients = Channel.CreateUnbounded<NetUDPv2Client>();

        public void StartServer(string address)
        {
            lock (this)
            {
                if (_socket != null)
                    throw new AlreadyException();

                var ep = V2Utilities.ParseIPAddress(address);
                _socket = new UdpClient(ep);
                _socket.Client.ReceiveBufferSize = V2Constants.SocketBufferSize;
                _socket.Client.SendBufferSize = V2Constants.SocketBufferSize;

                // Windows: отключаем SIO_UDP_CONNRESET — иначе ICMP "Port Unreachable"
                // от мёртвого клиента вызывает SocketException в ReceiveAsync(),
                // что убивает весь серверный receive loop.
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    const uint IOC_IN = 0x80000000;
                    const uint IOC_VENDOR = 0x18000000;
                    const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    _socket.Client.IOControl(unchecked((int)SIO_UDP_CONNRESET), new byte[] { 0 }, null);
                }

                _cts = new CancellationTokenSource();

                _ = Task.Run(ReceiveLoop);
            }
        }

        public void StopServer()
        {
            lock (this)
            {
                _cts?.Cancel();

                lock (_clientsLock)
                {
                    foreach (var client in Clients.Values)
                    {
                        try { client.Disconnect("Server stopped"); }
                        catch { }
                    }
                    Clients.Clear();
                }

                try { _socket?.Close(); }
                catch { }
                _socket = null;
            }
        }

        public async Task<INetworkClient> AcceptClient(CancellationToken token)
        {
            try
            {
                return await _acceptedClients.Reader.ReadAsync(token).ConfigureAwait(false);
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
                _socket?.Send(data, length, endPoint);
            }
            catch { }
        }

        /// <summary>
        /// Удалить клиента из списка
        /// </summary>
        internal void RemoveClient(NetUDPv2Client client, IPEndPoint endPoint)
        {
            var key = new EndPointKey(endPoint);
            lock (_clientsLock)
            {
                if (Clients.TryGetValue(key, out var existing) && existing == client)
                    Clients.Remove(key);
            }
        }

        private async Task ReceiveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break; // сокет закрыт — выходим
                }
                catch (SocketException)
                {
                    continue; // WSAECONNRESET и прочие — пропускаем, не ломаем loop
                }
                catch
                {
                    continue;
                }

                if (result.Buffer == null || result.Buffer.Length < 1)
                    continue;

                var type = (V2PacketType)result.Buffer[0];
                var key = new EndPointKey(result.RemoteEndPoint);

                if (type == V2PacketType.ConnectionRequest)
                {
                    HandleConnectionRequest(result.RemoteEndPoint, result.Buffer, result.Buffer.Length, key);
                }
                else
                {
                    NetUDPv2Client client = null;
                    lock (_clientsLock)
                    {
                        Clients.TryGetValue(key, out client);
                    }

                    try
                    {
                        client?.ProcessIncomingDatagram(result.Buffer, result.Buffer.Length);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void HandleConnectionRequest(IPEndPoint remoteEP, byte[] data, int length, EndPointKey key)
        {
            if (length < V2Headers.ConnectionRequestSize)
                return;

            V2Headers.ReadConnectionRequest(data, 0, out ushort version, out ulong token);

            // Проверяем версию протокола
            if (version != V2Constants.ProtocolVersion)
                return;

            lock (_clientsLock)
            {
                if (Clients.ContainsKey(key))
                    return; // уже подключён
            }

            var client = new NetUDPv2Client(this, remoteEP, MTU);

            lock (_clientsLock)
            {
                Clients[key] = client;
            }

            // Отправляем ConnectionAccept
            var buffer = new byte[V2Headers.ConnectionAcceptSize];
            V2Headers.WriteConnectionAccept(buffer, 0, token);
            SendTo(buffer, buffer.Length, remoteEP);

            _acceptedClients.Writer.TryWrite(client);
        }
    }
}
