using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMI.Network;
using EMI.NetUDP;
using EMI.NGC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Test.NetUDP
{
    [TestClass]
    public class Test_NetUDP
    {
        /// <summary>
        /// Порт увеличивается с каждым тестом чтобы избежать конфликтов
        /// </summary>
        private static int PortCounter = 40100;

        private static string GetAddress()
        {
            int port = Interlocked.Increment(ref PortCounter);
            return $"localhost#{port}";
        }

        #region Helpers

        private class TestEnv : IDisposable
        {
            public INetworkServer Server = null!;
            public INetworkClient ServerClient = null!;
            public INetworkClient Client = null!;
            public string Address = null!;

            public static async Task<TestEnv> CreateAsync(int timeoutMs = 5000)
            {
                var env = new TestEnv();
                env.Address = GetAddress();

                env.Server = NetUDPService.Service.GetNewServer();
                env.Server.StartServer(env.Address);

                env.Client = NetUDPService.Service.GetNewClient();

                var cts = new CancellationTokenSource(timeoutMs);
                var acceptTask = env.Server.AcceptClient(cts.Token);

                bool connected = await env.Client.Connect(env.Address, cts.Token);
                Assert.IsTrue(connected, "Client failed to connect");

                env.ServerClient = await acceptTask;
                Assert.IsNotNull(env.ServerClient, "Server failed to accept client");

                // Даём время на завершение инициализации
                await Task.Delay(100);

                return env;
            }

            public void Dispose()
            {
                try { Client?.Disconnect("test dispose"); } catch { }
                try { ServerClient?.Disconnect("test dispose"); } catch { }
                try { Server?.StopServer(); } catch { }
            }
        }

        #endregion

        #region Connection Tests

        [TestMethod]
        public async Task ConnectDisconnect()
        {
            using var env = await TestEnv.CreateAsync();

            Assert.IsTrue(env.Client.IsConnect);
            Assert.IsTrue(env.ServerClient.IsConnect);

            env.Client.Disconnect("test");
            await Task.Delay(200);

            Assert.IsFalse(env.Client.IsConnect);
        }

        [TestMethod]
        public async Task ServerDisconnect_ClientNotified()
        {
            using var env = await TestEnv.CreateAsync();

            bool clientDisconnected = false;
            env.Client.Disconnected += (err) => clientDisconnected = true;

            env.ServerClient.Disconnect("server disconnect");
            await Task.Delay(500);

            Assert.IsTrue(clientDisconnected, "Client should be notified of server disconnect");
        }

        [TestMethod]
        public async Task ConnectFail_WrongAddress()
        {
            var client = NetUDPService.Service.GetNewClient();
            var cts = new CancellationTokenSource(2000);

            // Подключаемся к порту на котором никто не слушает
            bool connected = await client.Connect("localhost#39999", cts.Token);
            Assert.IsFalse(connected, "Connection to non-existing server should fail");
        }

        [TestMethod]
        public async Task MultipleClients_Connect()
        {
            var address = GetAddress();
            var server = NetUDPService.Service.GetNewServer();
            server.StartServer(address);

            var cts = new CancellationTokenSource(5000);

            const int clientCount = 3;
            var clients = new INetworkClient[clientCount];
            var serverClients = new INetworkClient[clientCount];

            try
            {
                for (int i = 0; i < clientCount; i++)
                {
                    clients[i] = NetUDPService.Service.GetNewClient();
                    var acceptTask = server.AcceptClient(cts.Token);
                    bool connected = await clients[i].Connect(address, cts.Token);
                    Assert.IsTrue(connected, $"Client {i} failed to connect");
                    serverClients[i] = await acceptTask;
                    Assert.IsNotNull(serverClients[i], $"Server failed to accept client {i}");
                }

                // Все подключены
                for (int i = 0; i < clientCount; i++)
                {
                    Assert.IsTrue(clients[i].IsConnect);
                    Assert.IsTrue(serverClients[i].IsConnect);
                }
            }
            finally
            {
                for (int i = 0; i < clientCount; i++)
                {
                    try { clients[i]?.Disconnect("cleanup"); } catch { }
                    try { serverClients[i]?.Disconnect("cleanup"); } catch { }
                }
                try { server.StopServer(); } catch { }
            }
        }

        #endregion

        #region Unreliable Data Tests

        [TestMethod]
        public async Task SendReceive_Unreliable_SmallPacket()
        {
            using var env = await TestEnv.CreateAsync();

            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            var sendArray = new EasyArray(data);

            var cts = new CancellationTokenSource(5000);
            await env.Client.Send(sendArray, false, cts.Token);

            var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
            Assert.IsNotNull(received);
            Assert.IsFalse(received.IsEmpty());
            Assert.AreEqual(data.Length, received.Length);

            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], received.Bytes[i]);

            received.Dispose();
        }

        [TestMethod]
        public async Task SendReceive_Unreliable_Bidirectional()
        {
            using var env = await TestEnv.CreateAsync();

            var cts = new CancellationTokenSource(5000);

            // Client → Server
            byte[] data1 = { 10, 20, 30 };
            await env.Client.Send(new EasyArray(data1), false, cts.Token);

            var received1 = await env.ServerClient.AcceptPacket(1024, cts.Token);
            Assert.AreEqual(3, received1.Length);
            Assert.AreEqual(10, received1.Bytes[0]);
            received1.Dispose();

            // Server → Client
            byte[] data2 = { 40, 50, 60 };
            await env.ServerClient.Send(new EasyArray(data2), false, cts.Token);

            var received2 = await env.Client.AcceptPacket(1024, cts.Token);
            Assert.AreEqual(3, received2.Length);
            Assert.AreEqual(40, received2.Bytes[0]);
            received2.Dispose();
        }

        [TestMethod]
        public async Task SendReceive_Unreliable_MaxSinglePacket()
        {
            using var env = await TestEnv.CreateAsync();

            // Пакет максимального размера (без фрагментации)
            int size = UDPConstants.MaxUnreliablePayload;
            byte[] data = new byte[size];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);

            var cts = new CancellationTokenSource(5000);
            await env.Client.Send(new EasyArray(data), false, cts.Token);

            var received = await env.ServerClient.AcceptPacket(size + 100, cts.Token);
            Assert.AreEqual(size, received.Length);

            for (int i = 0; i < size; i++)
                Assert.AreEqual(data[i], received.Bytes[i], $"Mismatch at byte {i}");

            received.Dispose();
        }

        #endregion

        #region Reliable Data Tests

        [TestMethod]
        public async Task SendReceive_Reliable_SmallPacket()
        {
            using var env = await TestEnv.CreateAsync();

            byte[] data = { 100, 200, 150 };
            var cts = new CancellationTokenSource(5000);

            await env.Client.Send(new EasyArray(data), true, cts.Token);

            var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
            Assert.IsFalse(received.IsEmpty());
            Assert.AreEqual(data.Length, received.Length);

            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data[i], received.Bytes[i]);

            received.Dispose();
        }

        [TestMethod]
        public async Task SendReceive_Reliable_MultipleSequential()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(10000);

            const int packetCount = 10;
            for (int p = 0; p < packetCount; p++)
            {
                byte[] data = new byte[] { (byte)(p + 1), (byte)(p * 2) };
                await env.Client.Send(new EasyArray(data), true, cts.Token);

                var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
                Assert.IsFalse(received.IsEmpty());
                Assert.AreEqual(2, received.Length);
                Assert.AreEqual((byte)(p + 1), received.Bytes[0], $"Packet {p} byte 0 mismatch");
                Assert.AreEqual((byte)(p * 2), received.Bytes[1], $"Packet {p} byte 1 mismatch");
                received.Dispose();
            }
        }

        [TestMethod]
        public async Task SendReceive_Reliable_Bidirectional()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(5000);

            // Client → Server (reliable)
            byte[] data1 = { 11, 22, 33, 44 };
            await env.Client.Send(new EasyArray(data1), true, cts.Token);

            var received1 = await env.ServerClient.AcceptPacket(1024, cts.Token);
            Assert.AreEqual(4, received1.Length);
            Assert.AreEqual(11, received1.Bytes[0]);
            received1.Dispose();

            // Server → Client (reliable)
            byte[] data2 = { 55, 66, 77, 88 };
            await env.ServerClient.Send(new EasyArray(data2), true, cts.Token);

            var received2 = await env.Client.AcceptPacket(1024, cts.Token);
            Assert.AreEqual(4, received2.Length);
            Assert.AreEqual(55, received2.Bytes[0]);
            received2.Dispose();
        }

        #endregion

        #region Fragmentation Tests

        [TestMethod]
        public async Task SendReceive_Unreliable_Fragmented()
        {
            using var env = await TestEnv.CreateAsync();

            // Пакет больше MTU — будет фрагментирован
            int size = UDPConstants.MTU * 3;
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
                data[i] = (byte)(i % 256);

            var cts = new CancellationTokenSource(5000);
            await env.Client.Send(new EasyArray(data), false, cts.Token);

            var received = await env.ServerClient.AcceptPacket(size + 100, cts.Token);
            Assert.AreEqual(size, received.Length);

            for (int i = 0; i < size; i++)
                Assert.AreEqual(data[i], received.Bytes[i], $"Mismatch at byte {i}");

            received.Dispose();
        }

        [TestMethod]
        public async Task SendReceive_Reliable_Fragmented()
        {
            using var env = await TestEnv.CreateAsync();

            // Большой надёжный пакет
            int size = UDPConstants.MTU * 5;
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
                data[i] = (byte)((i * 7 + 3) % 256);

            var cts = new CancellationTokenSource(10000);
            await env.Client.Send(new EasyArray(data), true, cts.Token);

            var received = await env.ServerClient.AcceptPacket(size + 100, cts.Token);
            Assert.AreEqual(size, received.Length);

            for (int i = 0; i < size; i++)
                Assert.AreEqual(data[i], received.Bytes[i], $"Mismatch at byte {i}");

            received.Dispose();
        }

        [TestMethod]
        public async Task SendReceive_Reliable_LargeMessage()
        {
            using var env = await TestEnv.CreateAsync();

            // Большое сообщение — много фрагментов
            int size = 50000;
            byte[] data = new byte[size];
            var rng = new Random(42);
            rng.NextBytes(data);

            var cts = new CancellationTokenSource(15000);
            await env.Client.Send(new EasyArray(data), true, cts.Token);

            var received = await env.ServerClient.AcceptPacket(size + 100, cts.Token);
            Assert.AreEqual(size, received.Length);

            for (int i = 0; i < size; i++)
                Assert.AreEqual(data[i], received.Bytes[i], $"Mismatch at byte {i}");

            received.Dispose();
        }

        #endregion

        #region Mixed Mode Tests

        [TestMethod]
        public async Task SendReceive_Mixed_ReliableAndUnreliable()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(10000);

            // Отправляем чередуя надёжные и ненадёжные
            byte[] reliableData = { 0xAA, 0xBB };
            byte[] unreliableData = { 0xCC, 0xDD };

            await env.Client.Send(new EasyArray(reliableData), true, cts.Token);
            await env.Client.Send(new EasyArray(unreliableData), false, cts.Token);

            // Должны получить оба (в локальной среде потерь нет)
            var r1 = await env.ServerClient.AcceptPacket(1024, cts.Token);
            var r2 = await env.ServerClient.AcceptPacket(1024, cts.Token);

            Assert.IsFalse(r1.IsEmpty());
            Assert.IsFalse(r2.IsEmpty());

            r1.Dispose();
            r2.Dispose();
        }

        #endregion

        #region Performance / Throughput Tests

        [TestMethod]
        public async Task Throughput_ManySmallUnreliable()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(15000);

            const int packetCount = 100;
            byte[] data = new byte[64];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)i;

            // Отправляем быстро
            for (int i = 0; i < packetCount; i++)
            {
                data[0] = (byte)(i + 1);
                await env.Client.Send(new EasyArray(data), false, cts.Token);
            }

            // Получаем (некоторые могут не дойти в теории, но на localhost все должны)
            int receivedCount = 0;
            for (int i = 0; i < packetCount; i++)
            {
                var accept = Task.Run(async () =>
                {
                    try
                    {
                        var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
                        if (!received.IsEmpty())
                        {
                            received.Dispose();
                            return true;
                        }
                        return false;
                    }
                    catch
                    {
                        return false;
                    }
                });

                var timeout = Task.Delay(2000);
                var completed = await Task.WhenAny(accept, timeout);
                if (completed == accept && accept.Result)
                {
                    receivedCount++;
                }
                else
                {
                    break; // Больше пакетов нет
                }
            }

            Assert.IsTrue(receivedCount >= packetCount * 0.9,
                $"Expected at least 90% delivery on localhost, got {receivedCount}/{packetCount}");
        }

        [TestMethod]
        public async Task Throughput_ManySmallReliable()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(30000);

            const int packetCount = 50;

            for (int i = 0; i < packetCount; i++)
            {
                byte[] data = new byte[] { (byte)(i + 1), 0xFF };
                await env.Client.Send(new EasyArray(data), true, cts.Token);

                var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
                Assert.IsFalse(received.IsEmpty(), $"Packet {i} not received");
                Assert.AreEqual((byte)(i + 1), received.Bytes[0], $"Packet {i} data mismatch");
                received.Dispose();
            }
        }

        #endregion

        #region Address Tests

        [TestMethod]
        public async Task GetRemoteClientAddress_Valid()
        {
            using var env = await TestEnv.CreateAsync();

            var clientAddr = env.Client.GetRemoteClientAddress();
            var serverAddr = env.ServerClient.GetRemoteClientAddress();

            Assert.IsNotNull(clientAddr);
            Assert.IsNotNull(serverAddr);
            Assert.AreNotEqual("none", clientAddr);
            Assert.AreNotEqual("none", serverAddr);
            Assert.IsTrue(clientAddr.Contains("#"), "Address should contain '#' separator");
            Assert.IsTrue(serverAddr.Contains("#"), "Address should contain '#' separator");
        }

        #endregion

        #region NGCArray Tests

        [TestMethod]
        public async Task SendReceive_WithNGCArray()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(5000);

            // Тестируем с NGCArray (пулированным массивом)
            var ngcArray = new NGCArray(100);
            for (int i = 0; i < 100; i++)
                ngcArray.Bytes[i] = (byte)(i * 3);

            await env.Client.Send(ngcArray, true, cts.Token);
            ngcArray.Dispose();

            var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
            Assert.AreEqual(100, received.Length);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual((byte)(i * 3), received.Bytes[i]);

            received.Dispose();
        }

        #endregion

        #region DeliveredRate & Properties Tests

        [TestMethod]
        public async Task DeliveredRate_InitialValue()
        {
            using var env = await TestEnv.CreateAsync();
            await Task.Delay(50); // ensure async init

            // В начале DeliveredRate должен быть высоким (нет потерь)
            Assert.IsTrue(env.Client.DeliveredRate >= 0f);
            Assert.IsTrue(env.Client.DeliveredRate <= 1f);
        }

        [TestMethod]
        public void RandomDrop_DefaultValue()
        {
            var client = NetUDPService.Service.GetNewClient();
            Assert.AreEqual(RandomDropType.NoGuaranteed, client.RandomDrop);
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public async Task SendReceive_EmptyPayload_Reliable()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(5000);

            // Пустой пакет (0 байт данных) — должен корректно обрабатываться
            var emptyArray = new EasyArray(new byte[0]);
            await env.Client.Send(emptyArray, true, cts.Token);

            // Пустой reliable пакет дойдёт как пустой NGCArray — пробуем отправить следом нормальный
            byte[] data = { 42 };
            await env.Client.Send(new EasyArray(data), true, cts.Token);

            var received = await env.ServerClient.AcceptPacket(1024, cts.Token);
            // Может прийти пустой или нормальный — главное не крашиться
            Assert.IsNotNull(received);
            received.Dispose();
        }

        [TestMethod]
        public async Task SendAfterDisconnect_NoException()
        {
            using var env = await TestEnv.CreateAsync();

            env.Client.Disconnect("test");
            await Task.Delay(100);

            // Отправка после отключения не должна бросать исключение
            byte[] data = { 1, 2, 3 };
            var cts = new CancellationTokenSource(1000);
            await env.Client.Send(new EasyArray(data), true, cts.Token);
        }

        #endregion

        #region Stress Tests

        [TestMethod]
        public async Task Stress_ConcurrentSendReceive()
        {
            using var env = await TestEnv.CreateAsync();
            var cts = new CancellationTokenSource(20000);

            const int messageCount = 20;
            int receivedCount = 0;

            // Получатель
            var receiveTask = Task.Run(async () =>
            {
                for (int i = 0; i < messageCount; i++)
                {
                    try
                    {
                        var r = await env.ServerClient.AcceptPacket(1024, cts.Token);
                        if (!r.IsEmpty())
                        {
                            Interlocked.Increment(ref receivedCount);
                            r.Dispose();
                        }
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            });

            // Отправитель — параллельная отправка
            var sendTasks = new Task[messageCount];
            for (int i = 0; i < messageCount; i++)
            {
                int idx = i;
                sendTasks[i] = Task.Run(async () =>
                {
                    byte[] data = { (byte)(idx + 1), 0xAA, 0xBB };
                    await env.Client.Send(new EasyArray(data), true, cts.Token);
                });
            }

            await Task.WhenAll(sendTasks);
            await Task.WhenAny(receiveTask, Task.Delay(10000));

            Assert.IsTrue(receivedCount > 0, "Should receive at least some messages");
        }

        #endregion
    }
}
