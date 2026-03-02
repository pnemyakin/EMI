using EMI;
using EMI.Network.NetTCPV3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EMI.SyncInterface;

namespace TestSyncInterface
{
    public interface ITest
    {
        //[OnlyClient]
        Task<int> WaitAndGet();
    }

    class MyTest : ITest
    {
        public async Task<int> WaitAndGet()
        {
            Console.WriteLine("wait");
            await Task.Delay(5000);
            Console.WriteLine("return");
            return 404;
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            Run(args);
        }

        private static void Run(string[] args)
        {
            Client client = new Client(NetTCPV3Service.Service);
            
            var sync = new SyncInterface<ITest>("MyTest");
            sync.RegisterClass(client, new MyTest());

            //client
            if (args.Length == 0)
            {
                System.Diagnostics.Process.Start("server.bat");

                client = new Client(NetTCPV3Service.Service);
                client.Disconnected += Client_Disconnected;
            reconect:
                Console.WriteLine("Попытка подключиться...");//"31.10.114.169#25566"
                var status = client.Connect("127.0.0.1#25566", default).Result;
                if (status == false)
                {
                    Console.WriteLine("Не удалось подключиться...");
                    goto reconect;
                }
                Console.WriteLine("Успех");

                var ob = sync.NewIndicator(client);
                Console.WriteLine("Ждём");
                int a= ob.WaitAndGet().Result;
                Console.WriteLine("Вывод:" + a);
                Console.ReadLine();
            }//server
            else
            {
                Server server = new Server(NetTCPV3Service.Service);
                server.Start("any#25566");

                MyTest test = new MyTest();
                sync.RegisterClass(server, test);

                Console.WriteLine("Ожидание клиента");
                client = server.Accept().Result;
                client.Disconnected += Client_Disconnected;
                Console.WriteLine("Готово");

                
                Console.ReadLine();
            }
        }

        private static void Client_Disconnected(string error)
        {
            Console.WriteLine("Client_Disconnected => " + error);
        }
    }
}
