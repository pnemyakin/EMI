using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMI.Indicators;
using EMI.Network;
using EMI.Network.NetTCPV3;

namespace EMI.Test
{
    /// <summary>
    /// Интеграционные тесты: реальный TCP (NetTCPV3) + Server/Client + RPC + Middleware
    /// </summary>
    [TestClass]
    public class Test_Integration
    {
        // Используем разные порты для каждого теста чтобы избежать конфликтов
        private static int _nextPort = 37100;
        private static int GetPort() => Interlocked.Increment(ref _nextPort);

        /// <summary>
        /// Вспомогательный класс: поднимает Server + Client и соединяет их
        /// </summary>
        private class TestEnv : IDisposable
        {
            public Server Server = null!;
            public Client ServerClient = null!; // клиент на стороне сервера
            public Client Client = null!;       // клиент на стороне клиента
            public int Port;

            public static async Task<TestEnv> CreateAsync(
                bool useEncryption = false,
                bool useCompression = false,
                int timeoutMs = 10000)
            {
                var env = new TestEnv();
                env.Port = GetPort();

                env.Server = new Server(NetTCPV3Service.Service);
                env.Server.UseEncryption = useEncryption;
                env.Server.UseCompression = useCompression;
                env.Server.Start($"any#{env.Port}");

                env.Client = new Client(NetTCPV3Service.Service);
                env.Client.UseEncryption = useEncryption;
                env.Client.UseCompression = useCompression;

                using var cts = new CancellationTokenSource(timeoutMs);

                var acceptTask = env.Server.Accept();
                var connectResult = await env.Client.Connect($"127.0.0.1#{env.Port}", cts.Token);

                if (!connectResult)
                    throw new Exception("Client.Connect returned false");

                env.ServerClient = await acceptTask;
                return env;
            }

            public void Dispose()
            {
                try { Client?.Disconnect("test cleanup"); } catch { }
                try { Server?.Stop(); } catch { }
            }
        }

        #region Connect / Disconnect

        [TestMethod("Интеграция: подключение и отключение")]
        [Timeout(15000)]
        public async Task Integration_ConnectDisconnect()
        {
            using var env = await TestEnv.CreateAsync();

            Assert.IsTrue(env.Client.IsConnect, "Client should be connected");
            Assert.IsTrue(env.ServerClient.IsConnect, "ServerClient should be connected");
            Assert.IsTrue(env.ServerClient.IsServerSide, "ServerClient should be server-side");
            Assert.IsFalse(env.Client.IsServerSide, "Client should not be server-side");

            env.Client.Disconnect("test done");
            await Task.Delay(200); // дождаться обработки disconnect

            Assert.IsFalse(env.Client.IsConnect, "Client should be disconnected");
        }

        [TestMethod("Интеграция: Disconnected событие срабатывает")]
        [Timeout(15000)]
        public async Task Integration_DisconnectedEvent()
        {
            using var env = await TestEnv.CreateAsync();

            var disconnectedTcs = new TaskCompletionSource<string>();
            env.ServerClient.Disconnected += (error) => disconnectedTcs.TrySetResult(error);

            env.Client.Disconnect("bye");

            using var cts = new CancellationTokenSource(5000);
            cts.Token.Register(() => disconnectedTcs.TrySetCanceled());

            var reason = await disconnectedTcs.Task;
            Assert.IsNotNull(reason, "Disconnected event should fire with a reason");
        }

        #endregion

        #region RPC без параметров

        [TestMethod("Интеграция: RPC вызов без параметров (Guaranteed)")]
        [Timeout(15000)]
        public async Task Integration_RPC_NoParams()
        {
            using var env = await TestEnv.CreateAsync();

            bool methodCalled = false;
            var indicator = new Indicator.Func("TestNoParams");

            // Регистрируем на стороне клиента
            env.Client.LocalRPC.RegisterMethod(() => { methodCalled = true; }, indicator);

            // Сервер вызывает метод на клиенте
            await indicator.RCall(env.ServerClient, RCType.Guaranteed);
            await Task.Delay(300);

            Assert.IsTrue(methodCalled, "RPC method should have been called");
        }

        #endregion

        #region RPC с параметрами

        [TestMethod("Интеграция: RPC вызов с параметрами string")]
        [Timeout(15000)]
        public async Task Integration_RPC_WithStringParam()
        {
            using var env = await TestEnv.CreateAsync();

            string? receivedMessage = null;
            var indicator = new Indicator.Func<string>("SendMessage");

            env.Client.LocalRPC.RegisterMethod<string>((msg) =>
            {
                receivedMessage = msg;
            }, indicator);

            await indicator.RCall("Hello from server!", env.ServerClient, RCType.Guaranteed);
            await Task.Delay(300);

            Assert.AreEqual("Hello from server!", receivedMessage);
        }

        [TestMethod("Интеграция: RPC вызов с несколькими параметрами")]
        [Timeout(15000)]
        public async Task Integration_RPC_MultipleParams()
        {
            using var env = await TestEnv.CreateAsync();

            int receivedA = 0;
            float receivedB = 0;
            var indicator = new Indicator.Func<int, float>("MathOp");

            env.Client.LocalRPC.RegisterMethod<int, float>((a, b) =>
            {
                receivedA = a;
                receivedB = b;
            }, indicator);

            await indicator.RCall(42, 3.14f, env.ServerClient, RCType.Guaranteed);
            await Task.Delay(300);

            Assert.AreEqual(42, receivedA);
            Assert.AreEqual(3.14f, receivedB, 0.001f);
        }

        #endregion

        #region RPC с возвращаемым значением

        [TestMethod("Интеграция: RPC вызов с возвращаемым значением")]
        [Timeout(15000)]
        public async Task Integration_RPC_ReturnValue()
        {
            using var env = await TestEnv.CreateAsync();

            var indicator = new Indicator.FuncOut<int, int, int>("Add");

            env.Client.LocalRPC.RegisterMethod<int, int, int>((a, b) => a + b, indicator);

            var result = await indicator.RCall(10, 32, env.ServerClient, RCType.ReturnWait);

            Assert.AreEqual(42, result);
        }

        [TestMethod("Интеграция: RPC возвращаемое string значение")]
        [Timeout(15000)]
        public async Task Integration_RPC_ReturnString()
        {
            using var env = await TestEnv.CreateAsync();

            var indicator = new Indicator.FuncOut<string>("GetGreeting");

            env.Client.LocalRPC.RegisterMethod<string>(() => "Привет, мир!", indicator);

            var result = await indicator.RCall(env.ServerClient, RCType.ReturnWait);

            Assert.AreEqual("Привет, мир!", result);
        }

        #endregion

        #region RPC двунаправленный

        [TestMethod("Интеграция: RPC в обе стороны")]
        [Timeout(15000)]
        public async Task Integration_RPC_Bidirectional()
        {
            using var env = await TestEnv.CreateAsync();

            // Сервер -> клиент
            string? clientReceived = null;
            var toClient = new Indicator.Func<string>("ToClient");
            env.Client.LocalRPC.RegisterMethod<string>((msg) =>
            {
                clientReceived = msg;
            }, toClient);

            // Клиент -> сервер
            string? serverReceived = null;
            var toServer = new Indicator.Func<string>("ToServer");
            env.Server.RPC.RegisterMethod<string>((msg) =>
            {
                serverReceived = msg;
            }, toServer);

            await toClient.RCall("ping", env.ServerClient, RCType.Guaranteed);
            await toServer.RCall("pong", env.Client, RCType.Guaranteed);
            await Task.Delay(300);

            Assert.AreEqual("ping", clientReceived, "Client should receive 'ping'");
            Assert.AreEqual("pong", serverReceived, "Server should receive 'pong'");
        }

        #endregion

        #region Множественные вызовы

        [TestMethod("Интеграция: множество RPC вызовов подряд")]
        [Timeout(15000)]
        public async Task Integration_RPC_MultipleCallsSequential()
        {
            using var env = await TestEnv.CreateAsync();

            var received = new List<int>();
            var indicator = new Indicator.Func<int>("Counter");

            env.Client.LocalRPC.RegisterMethod<int>((n) =>
            {
                lock (received)
                    received.Add(n);
            }, indicator);

            const int count = 50;
            for (int i = 0; i < count; i++)
            {
                await indicator.RCall(i, env.ServerClient, RCType.Guaranteed);
            }

            // Ждём доставки всех пакетов
            for (int retry = 0; retry < 50 && received.Count < count; retry++)
                await Task.Delay(100);

            Assert.AreEqual(count, received.Count, $"Should receive {count} calls, got {received.Count}");

            // Проверяем что все значения дошли (порядок guaranteed)
            received.Sort();
            for (int i = 0; i < count; i++)
                Assert.AreEqual(i, received[i], $"Value at index {i} mismatch");
        }

        #endregion

        #region Middleware: шифрование

        [TestMethod("Интеграция: RPC с UseEncryption")]
        [Timeout(15000)]
        public async Task Integration_RPC_WithEncryption()
        {
            using var env = await TestEnv.CreateAsync(useEncryption: true);

            var indicator = new Indicator.FuncOut<string, string>("Echo");

            env.Client.LocalRPC.RegisterMethod<string, string>(
                (msg) => $"echo: {msg}", indicator);

            var result = await indicator.RCall("secret", env.ServerClient, RCType.ReturnWait);

            Assert.AreEqual("echo: secret", result);
        }

        #endregion

        #region Middleware: сжатие

        [TestMethod("Интеграция: RPC с UseCompression")]
        [Timeout(15000)]
        public async Task Integration_RPC_WithCompression()
        {
            using var env = await TestEnv.CreateAsync(useCompression: true);

            var indicator = new Indicator.FuncOut<string, string>("Echo");

            env.Client.LocalRPC.RegisterMethod<string, string>(
                (msg) => $"compressed: {msg}", indicator);

            var result = await indicator.RCall("data", env.ServerClient, RCType.ReturnWait);

            Assert.AreEqual("compressed: data", result);
        }

        #endregion

        #region Middleware: шифрование + сжатие

        [TestMethod("Интеграция: RPC с UseEncryption + UseCompression")]
        [Timeout(15000)]
        public async Task Integration_RPC_WithEncryptionAndCompression()
        {
            using var env = await TestEnv.CreateAsync(useEncryption: true, useCompression: true);

            // Тест с параметрами
            int receivedValue = 0;
            var indicator = new Indicator.Func<int>("SetValue");
            env.Client.LocalRPC.RegisterMethod<int>((v) => { receivedValue = v; }, indicator);

            await indicator.RCall(12345, env.ServerClient, RCType.Guaranteed);
            await Task.Delay(300);

            Assert.AreEqual(12345, receivedValue);

            // Тест с возвращаемым значением
            var echoIndicator = new Indicator.FuncOut<string, string>("EchoFull");
            env.Client.LocalRPC.RegisterMethod<string, string>(
                (msg) => $"encrypted+compressed: {msg}", echoIndicator);

            var result = await echoIndicator.RCall("payload", env.ServerClient, RCType.ReturnWait);
            Assert.AreEqual("encrypted+compressed: payload", result);
        }

        #endregion

        #region RPC метод не найден (логирование)

        [TestMethod("Интеграция: RPC вызов незарегистрированного метода — не падает")]
        [Timeout(15000)]
        public async Task Integration_RPC_MethodNotFound_NoException()
        {
            using var env = await TestEnv.CreateAsync();

            // Вызываем метод который нигде не зарегистрирован
            var indicator = new Indicator.Func("NonExistentMethod");

            // Не должно упасть — просто логируется предупреждение
            await indicator.RCall(env.ServerClient, RCType.Guaranteed);
            await Task.Delay(300);

            // Если дошли сюда — тест пройден (нет исключения, нет disconnect)
            Assert.IsTrue(env.Client.IsConnect, "Client should still be connected");
        }

        #endregion

        #region Server.RPC (глобальный)

        [TestMethod("Интеграция: Server.RPC — один обработчик на все клиенты")]
        [Timeout(15000)]
        public async Task Integration_ServerRPC_Shared()
        {
            int port = GetPort();
            var server = new Server(NetTCPV3Service.Service);
            server.Start($"any#{port}");

            try
            {
                int callCount = 0;
                var indicator = new Indicator.Func("SharedMethod");
                server.RPC.RegisterMethod(() =>
                {
                    Interlocked.Increment(ref callCount);
                }, indicator);

                // Два клиента подключаются
                var client1 = new Client(NetTCPV3Service.Service);
                var client2 = new Client(NetTCPV3Service.Service);

                using var cts = new CancellationTokenSource(10000);

                var accept1 = server.Accept();
                await client1.Connect($"127.0.0.1#{port}", cts.Token);
                var sc1 = await accept1;

                var accept2 = server.Accept();
                await client2.Connect($"127.0.0.1#{port}", cts.Token);
                var sc2 = await accept2;

                // Оба клиента вызывают один и тот же метод на сервере
                await indicator.RCall(client1, RCType.Guaranteed);
                await indicator.RCall(client2, RCType.Guaranteed);
                await Task.Delay(500);

                Assert.AreEqual(2, callCount, "Shared method should be called twice");

                client1.Disconnect("done");
                client2.Disconnect("done");
            }
            finally
            {
                server.Stop();
            }
        }

        #endregion

        #region Ping

        [TestMethod("Интеграция: Ping обновляется после подключения")]
        [Timeout(25000)]
        public async Task Integration_Ping_Updates()
        {
            int port = GetPort();
            var server = new Server(NetTCPV3Service.Service);
            server.PingPollingInterval = TimeSpan.FromSeconds(2);
            server.Start($"any#{port}");

            var client = new Client(NetTCPV3Service.Service);
            client.PingPollingInterval = TimeSpan.FromSeconds(2);

            try
            {
                using var cts = new CancellationTokenSource(10000);
                var acceptTask = server.Accept();
                var connected = await client.Connect($"127.0.0.1#{port}", cts.Token);
                Assert.IsTrue(connected, "Client should connect");
                var serverClient = await acceptTask;

                // Ждём ~4 секунды чтобы ping прошёл (интервал = 2с)
                await Task.Delay(4500);

                // Ping должен быть > 0 (реальный TCP loopback)
                Assert.IsTrue(client.Ping > TimeSpan.Zero, 
                    $"Ping should be > 0, got {client.Ping.TotalMilliseconds}ms");
                Assert.IsTrue(client.Ping < TimeSpan.FromSeconds(5),
                    $"Ping should be < 5s on loopback, got {client.Ping.TotalMilliseconds}ms");

                client.Disconnect("done");
            }
            finally
            {
                server.Stop();
            }
        }

        #endregion
    }
}
