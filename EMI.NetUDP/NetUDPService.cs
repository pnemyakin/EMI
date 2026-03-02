using EMI.Network;

namespace EMI.NetUDP
{
    /// <summary>
    /// Сервис UDP транспорта — фабрика клиентов и серверов
    /// </summary>
    public class NetUDPService : INetworkService
    {
        /// <summary>
        /// Статический экземпляр сервиса
        /// </summary>
        public readonly static NetUDPService Service = new NetUDPService();

        public INetworkClient GetNewClient()
        {
            return new NetUDPClient();
        }

        public INetworkServer GetNewServer()
        {
            return new NetUDPServer();
        }
    }
}
