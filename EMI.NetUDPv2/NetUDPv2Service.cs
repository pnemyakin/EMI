using EMI.Network;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// Сервис UDP v2 транспорта — фабрика клиентов и серверов.
    /// Использование:
    /// <code>
    /// var server = new Server(NetUDPv2Service.Service);
    /// var client = new Client(NetUDPv2Service.Service);
    /// </code>
    /// </summary>
    public class NetUDPv2Service : INetworkService
    {
        /// <summary>
        /// Статический экземпляр сервиса
        /// </summary>
        public static readonly NetUDPv2Service Service = new NetUDPv2Service();

        public INetworkClient GetNewClient()
        {
            return new NetUDPv2Client();
        }

        public INetworkServer GetNewServer()
        {
            return new NetUDPv2Server();
        }
    }
}
