using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EMI;
using EMI.Indicators;
using EMI.Network;
using EMI.Network.NetTCPV3;
using EMI.SyncInterface;
using EMI.MyException;

namespace Test.SyncInterface
{
    #region Test Interfaces

    public interface ISimpleService
    {
        void DoWork();
        Task DoWorkAsync();
        Task<int> GetValueAsync();
        int GetValue();
    }

    public interface IWithAttributes
    {
        [OnlyClient]
        void ClientOnly();

        [OnlyServer]
        void ServerOnly();

        void BothSides();
    }

    public interface IWithRCTypeOption
    {
        [RCTypeOption(RCType.Fast)]
        void FastMethod();

        [RCTypeOption(RCType.ReturnWait)]
        Task<int> WaitMethod();

        void DefaultMethod();
    }

    public interface IWithProperties
    {
        int Value { get; set; }
    }

    public interface IMultiParam
    {
        Task<int> Add(int a, int b);
        void Send(string msg, int count);
    }

    /// <summary>
    /// Интерфейс с CancellationToken в последнем параметре — токен должен пробрасываться
    /// в RCall, а не сериализоваться как данные.
    /// </summary>
    public interface IWithCancellation
    {
        Task<int> GetValueAsync(int id, CancellationToken ct);
        Task DoWorkAsync(string data, CancellationToken ct);
        void DoSync(int x, CancellationToken ct);
    }

    public class SimpleServiceImpl : ISimpleService
    {
        public int WorkCount = 0;
        public int GetValueResult = 42;

        public void DoWork() => Interlocked.Increment(ref WorkCount);
        public async Task DoWorkAsync()
        {
            Interlocked.Increment(ref WorkCount);
            await Task.CompletedTask;
        }
        public async Task<int> GetValueAsync()
        {
            await Task.Delay(10);
            return GetValueResult;
        }
        public int GetValue() => GetValueResult;
    }

    public class MultiParamImpl : IMultiParam
    {
        public async Task<int> Add(int a, int b)
        {
            await Task.CompletedTask;
            return a + b;
        }

        public void Send(string msg, int count) { }
    }

    public class WithCancellationImpl : IWithCancellation
    {
        public int LastId = 0;
        public string LastData = "";
        public int SyncValue = 0;

        public async Task<int> GetValueAsync(int id, CancellationToken ct)
        {
            LastId = id;
            await Task.Delay(10, ct);
            return id * 10;
        }

        public async Task DoWorkAsync(string data, CancellationToken ct)
        {
            LastData = data;
            await Task.CompletedTask;
        }

        public void DoSync(int x, CancellationToken ct)
        {
            SyncValue = x;
        }
    }

    public class WithAttributesImpl : IWithAttributes
    {
        public int ClientCallCount = 0;
        public int ServerCallCount = 0;
        public int BothCallCount = 0;

        public void ClientOnly() => Interlocked.Increment(ref ClientCallCount);
        public void ServerOnly() => Interlocked.Increment(ref ServerCallCount);
        public void BothSides() => Interlocked.Increment(ref BothCallCount);
    }

    #endregion

    [TestClass]
    public class Test_SyncInterface_Construction
    {
        [TestMethod("SyncInterface конструктор работает для валидного интерфейса")]
        public void Constructor_ValidInterface_Succeeds()
        {
            var sync = new SyncInterface<ISimpleService>("Test1");
            Assert.IsNotNull(sync);
        }

        [TestMethod("SyncInterface выбрасывает исключение для не-интерфейса")]
        public void Constructor_NotInterface_Throws()
        {
            Assert.ThrowsException<InvalidInterfaceException>(() =>
            {
                new SyncInterface<SimpleServiceImpl>("BadType");
            });
        }

        [TestMethod("SyncInterface выбрасывает исключение для интерфейса со свойствами")]
        public void Constructor_WithProperties_Throws()
        {
            Assert.ThrowsException<InvalidInterfaceException>(() =>
            {
                new SyncInterface<IWithProperties>("PropTest");
            });
        }

        [TestMethod("SyncInterface кеширует типы при повторном создании")]
        public void Constructor_CachesTypes()
        {
            // Используем уникальное имя для первого, второе создание того же типа
            // должно переиспользовать кеш (не упасть из-за двойного DefineType)
            var sync1 = new SyncInterface<IWithAttributes>("CacheTest1");
            var sync2 = new SyncInterface<IWithAttributes>("CacheTest2");
            Assert.IsNotNull(sync1);
            Assert.IsNotNull(sync2);
        }

        [TestMethod("SyncInterface с мультипараметрами создаётся")]
        public void Constructor_MultiParam_Succeeds()
        {
            var sync = new SyncInterface<IMultiParam>("MultiParam");
            Assert.IsNotNull(sync);
        }

        [TestMethod("SyncInterface с CancellationToken в параметрах создаётся")]
        public void Constructor_WithCancellation_Succeeds()
        {
            var sync = new SyncInterface<IWithCancellation>("CTTest");
            Assert.IsNotNull(sync);
        }

        [TestMethod("SyncInterface с атрибутами RCTypeOption создаётся")]
        public void Constructor_WithRCTypeOption_Succeeds()
        {
            var sync = new SyncInterface<IWithRCTypeOption>("RCTypeTest");
            Assert.IsNotNull(sync);
        }
    }

    [TestClass]
    public class Test_SyncInterface_Integration
    {
        private static int _nextPort = 38200;
        private static int GetPort() => Interlocked.Increment(ref _nextPort);

        private class TestEnv : IDisposable
        {
            public Server Server = null!;
            public Client ServerClient = null!;
            public Client Client = null!;
            public int Port;

            public static async Task<TestEnv> CreateAsync(int timeoutMs = 10000)
            {
                var env = new TestEnv();
                env.Port = GetPort();

                env.Server = new Server(NetTCPV3Service.Service);
                env.Server.Start($"any#{env.Port}");

                env.Client = new Client(NetTCPV3Service.Service);

                using var cts = new CancellationTokenSource(timeoutMs);

                var acceptTask = env.Server.Accept();
                var connectResult = await env.Client.Connect($"127.0.0.1#{env.Port}", cts.Token);
                if (!connectResult)
                    throw new Exception("Client.Connect failed");

                env.ServerClient = await acceptTask;
                return env;
            }

            public void Dispose()
            {
                try { Client?.Disconnect("test cleanup"); } catch { }
                try { Server?.Stop(); } catch { }
            }
        }

        [TestMethod("NewIndicator создаёт прокси для клиентской стороны")]
        [Timeout(15000)]
        public async Task NewIndicator_ClientSide_CreatesProxy()
        {
            using var env = await TestEnv.CreateAsync();
            var sync = new SyncInterface<ISimpleService>("IntTest1");

            var proxy = sync.NewIndicator(env.Client);
            Assert.IsNotNull(proxy);
            Assert.IsTrue(proxy is ISimpleService);
        }

        [TestMethod("NewIndicator создаёт прокси для серверной стороны")]
        [Timeout(15000)]
        public async Task NewIndicator_ServerSide_CreatesProxy()
        {
            using var env = await TestEnv.CreateAsync();
            var sync = new SyncInterface<ISimpleService>("IntTest2");

            var proxy = sync.NewIndicator(env.ServerClient);
            Assert.IsNotNull(proxy);
            Assert.IsTrue(proxy is ISimpleService);
        }

        [TestMethod("RegisterClass + вызов void метода через прокси")]
        [Timeout(15000)]
        public async Task RegisterAndCall_VoidMethod()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<ISimpleService>("VoidCall");
            var impl = new SimpleServiceImpl();

            // Регистрируем реализацию на сервере — клиент будет вызывать
            sync.RegisterClass(env.Server, impl);

            // Создаём прокси на стороне клиента
            var proxy = sync.NewIndicator(env.Client);

            // Вызываем async версию (Task)
            await proxy.DoWorkAsync();
            await Task.Delay(200); // дать время обработать

            Assert.IsTrue(impl.WorkCount > 0, "Void async method should have been called");
        }

        [TestMethod("RegisterClass + вызов async Task<int> метода через прокси")]
        [Timeout(15000)]
        public async Task RegisterAndCall_AsyncReturnMethod()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<ISimpleService>("AsyncRet");
            var impl = new SimpleServiceImpl { GetValueResult = 777 };

            sync.RegisterClass(env.Server, impl);
            var proxy = sync.NewIndicator(env.Client);

            int result = await proxy.GetValueAsync();
            Assert.AreEqual(777, result);
        }

        [TestMethod("RegisterClass + вызов async Task<int> Add(int,int)")]
        [Timeout(15000)]
        public async Task RegisterAndCall_MultiParam_AsyncReturn()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<IMultiParam>("MultiAdd");
            var impl = new MultiParamImpl();

            sync.RegisterClass(env.Server, impl);
            var proxy = sync.NewIndicator(env.Client);

            int result = await proxy.Add(10, 32);
            Assert.AreEqual(42, result);
        }

        [TestMethod("RegisterClass работает после кеширования (второй SyncInterface<T>)")]
        [Timeout(15000)]
        public async Task RegisterClass_WorksAfterCaching()
        {
            using var env = await TestEnv.CreateAsync();

            // Первый — просто чтоб закешировать тип
            var sync1 = new SyncInterface<ISimpleService>("Cache1");
            // Второй — из кеша — должен тоже уметь RegisterClass
            var sync2 = new SyncInterface<ISimpleService>("Cache2");

            var impl = new SimpleServiceImpl { GetValueResult = 999 };
            sync2.RegisterClass(env.Server, impl);

            var proxy = sync2.NewIndicator(env.Client);
            int result = await proxy.GetValueAsync();
            Assert.AreEqual(999, result);
        }

        [TestMethod("CancellationToken: async Task<T> метод пробрасывает токен и работает")]
        [Timeout(15000)]
        public async Task CancellationToken_AsyncReturn_Works()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<IWithCancellation>("CTReturn");
            var impl = new WithCancellationImpl();

            sync.RegisterClass(env.Server, impl);
            var proxy = sync.NewIndicator(env.Client);

            int result = await proxy.GetValueAsync(5, CancellationToken.None);
            Assert.AreEqual(50, result);
        }

        [TestMethod("CancellationToken: async Task метод работает")]
        [Timeout(15000)]
        public async Task CancellationToken_AsyncVoid_Works()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<IWithCancellation>("CTVoid");
            var impl = new WithCancellationImpl();

            sync.RegisterClass(env.Server, impl);
            var proxy = sync.NewIndicator(env.Client);

            await proxy.DoWorkAsync("hello", CancellationToken.None);
            await Task.Delay(200);

            Assert.AreEqual("hello", impl.LastData);
        }

        [TestMethod("CancellationToken: отмена через токен отменяет ожидание RPC")]
        [Timeout(15000)]
        public async Task CancellationToken_Cancellation_StopsWaiting()
        {
            using var env = await TestEnv.CreateAsync();

            var sync = new SyncInterface<IWithCancellation>("CTCancel");
            // Не регистрируем реализацию на сервере — RPC зависнет, но токен должен отменить

            var proxy = sync.NewIndicator(env.Client);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var ex = await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            {
                await proxy.GetValueAsync(1, cts.Token);
            });
        }
    }
}
