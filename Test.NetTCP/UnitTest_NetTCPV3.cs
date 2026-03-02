
using EMI.NGC;
using System.Diagnostics;

namespace Test.NetTCP
{
    [TestClass]
    public class UnitTest_NetTCPV3
    {
        readonly static INetworkService Service = NetTCPV3Service.Service;
        const int Port = 30000;

        [TestMethod("�������� �����������")]
        public void Test1()
        {
            var env = new TestService();
            try
            {
                var status = env.Connect(Service, Port);
                Assert.IsTrue(status);
                Assert.IsTrue(env.Client1.IsConnect);
                Assert.IsTrue(env.Client2.IsConnect);
                Assert.IsTrue(status);
            }
            finally
            {
                env.Stop();
            }
        }

        [TestMethod("��������� ������ (���������)")]
        public void Test2()
        {
            CancellationTokenSource cts = new(5000);
            var env = new TestService();
            try
            {
                env.Connect(Service, Port);
                var array = new NGCArray(512);

                Random random=new();
                random.NextBytes(array.Bytes);

                env.Client1.Send(array, true, cts.Token);
                var acceptArray = env.Client2.AcceptPacket(array.Length, cts.Token).Result;

                CollectionAssert.AreEqual(array.Bytes, acceptArray.Bytes);

                array.Dispose();
                acceptArray.Dispose();
            }
            finally
            {
                env.Stop();
            }
        }

        [TestMethod("��������� ������ (�������)")]
        public void Test3()
        {
            CancellationTokenSource cts = new(60000);
            var env = new TestService();
            try
            {
                env.Connect(Service, Port);
                var array = new NGCArray(1024*1024*256); //256 MB

                Random random = new();
                random.NextBytes(array.Bytes);

                _ = env.Client1.Send(array, true, cts.Token);
                var acceptArray = env.Client2.AcceptPacket(array.Length, cts.Token).Result;

                Assert.AreEqual(array.Length, acceptArray.Length);
                byte[] a = array.Bytes;
                byte[] b = acceptArray.Bytes;

                for(int i=0;i < array.Length; i++)
                {
                    if (a[i] != b[i])
                    {
                        Assert.Fail();
                    }
                }

                array.Dispose();
                acceptArray.Dispose();
            }
            finally
            {
                env.Stop();
            }
        }
    }

    public class TestService
    {
        public INetworkServer Server = null!;
        public INetworkClient Client1 = null!;
        public INetworkClient Client2 = null!;

        public bool Connect(INetworkService service, int port)
        {
            try
            {
                CancellationTokenSource cts = new (5000);

                Server = service.GetNewServer();
                Client1 = service.GetNewClient();

                Server.StartServer("any#" + port);
                Task<bool> status = Client1.Connect("localhost#" + port, cts.Token);

                Client2 = Server.AcceptClient(cts.Token).Result;
                return status.Result;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                Client1.Disconnect("");
            }
            catch { }
            try
            {
                Server.StopServer();
            }
            catch { }
        }
    }
}