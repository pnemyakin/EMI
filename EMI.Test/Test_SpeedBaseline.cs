using System.Diagnostics;

namespace EMI.Test
{
    using EMI.NGC;
    using EMI.Indicators;

    /// <summary>
    /// РР·РјРµСЂРµРЅРёСЏ РїСЂРѕРёР·РІРѕРґРёС‚РµР»СЊРЅРѕСЃС‚Рё (speed tests).
    /// 
    /// Р­С‚Рё С‚РµСЃС‚С‹ Р·Р°РїРёСЃС‹РІР°СЋС‚ throughput РІ РєРѕРЅСЃРѕР»СЊ. Р—Р°РїСѓСЃС‚РёС‚СЊ:
    ///   dotnet test --filter "ClassName~Test_SpeedBaseline" -v normal
    /// 
    /// Baseline РЅСѓР¶РЅРѕ Р·Р°РїРёСЃР°С‚СЊ Р”Рћ СЂРµС„Р°РєС‚РѕСЂРёРЅРіР°.
    /// РџРѕСЃР»Рµ РєР°Р¶РґРѕРіРѕ РёР·РјРµРЅРµРЅРёСЏ (Channel, ConcurrentDictionary, etc.) вЂ” РїРµСЂРµР·Р°РїСѓСЃС‚РёС‚СЊ Рё СЃСЂР°РІРЅРёС‚СЊ.
    /// 
    /// РљР°Р¶РґС‹Р№ С‚РµСЃС‚ РїСЂРѕС…РѕРґРёС‚ assert РЅР° РјРёРЅРёРјР°Р»СЊРЅС‹Р№ РїРѕСЂРѕРі вЂ” РµСЃР»Рё РїРѕСЃР»Рµ Р·Р°РјРµРЅС‹ СЃС‚Р°Р»Рѕ
    /// РІ 10x РјРµРґР»РµРЅРЅРµРµ, С‚РµСЃС‚ СѓРїР°РґС‘С‚.
    /// </summary>
    [TestClass]
    public class Test_SpeedBaseline
    {
        // Warmup iterations РѕС‚РґРµР»СЊРЅРѕ РѕС‚ Р·Р°РјРµСЂР°
        private const int WARMUP = 50;
        private static readonly string ResultsFile = Path.Combine(
            Path.GetDirectoryName(typeof(Test_SpeedBaseline).Assembly.Location)!, 
            "speed_results.txt");

        private static readonly object FileLock = new();

        private static void LogResult(string line)
        {
            Console.WriteLine(line);
            lock (FileLock)
            {
                File.AppendAllText(ResultsFile, line + Environment.NewLine);
            }
        }

        [ClassInitialize]
        public static void ClearResultsFile(TestContext context)
        {
            if (File.Exists(ResultsFile))
                File.Delete(ResultsFile);
        }

        #region FixedStack Speed

        [TestMethod("SPEED: FixedStack sequential Push+Pop throughput")]
        public async Task Speed_FixedStack_SequentialPushPop()
        {
            const int size = 1000;
            const int iterations = 50_000;
            var stack = new FixedStack<int>(size, TimeSpan.FromSeconds(10));

            // Warmup
            for (int i = 0; i < WARMUP; i++)
            {
                await stack.Push(i, default);
            }
            for (int i = 0; i < WARMUP; i++)
            {
                await stack.Pop(default);
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                await stack.Push(i, default);
                await stack.Pop(default);
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[FixedStack Sequential] {iterations} push+pop pairs in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            // РњРёРЅРёРјР°Р»СЊРЅС‹Р№ РїРѕСЂРѕРі: 10K ops/sec (РЅР° СЃР»Р°Р±РѕР№ РјР°С€РёРЅРµ)
            Assert.IsTrue(opsPerSec > 10_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: FixedStack concurrent Push+Pop throughput")]
        public async Task Speed_FixedStack_ConcurrentPushPop()
        {
            const int stackSize = 100;
            const int totalItems = 100_000;
            var stack = new FixedStack<int>(stackSize, TimeSpan.FromSeconds(30));
            var cts = new CancellationTokenSource(60000);

            // Warmup
            for (int i = 0; i < WARMUP; i++)
            {
                await stack.Push(i, default);
            }
            for (int i = 0; i < WARMUP; i++)
            {
                await stack.Pop(default);
            }

            var sw = Stopwatch.StartNew();

            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                    await stack.Push(i, cts.Token);
            });

            var consumer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                    await stack.Pop(cts.Token);
            });

            await Task.WhenAll(producer, consumer);
            sw.Stop();

            double opsPerSec = totalItems / sw.Elapsed.TotalSeconds;
            LogResult($"[FixedStack Concurrent] {totalItems} items in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 5_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: FixedStack 4-producer 4-consumer throughput")]
        public async Task Speed_FixedStack_4P4C()
        {
            const int stackSize = 100;
            const int itemsPerThread = 25_000;
            const int threadCount = 4;
            var stack = new FixedStack<int>(stackSize, TimeSpan.FromSeconds(30));
            var cts = new CancellationTokenSource(60000);

            var sw = Stopwatch.StartNew();

            var producers = Enumerable.Range(0, threadCount).Select(t =>
                Task.Run(async () =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                        await stack.Push(i, cts.Token);
                })).ToArray();

            var consumers = Enumerable.Range(0, threadCount).Select(t =>
                Task.Run(async () =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                        await stack.Pop(cts.Token);
                })).ToArray();

            await Task.WhenAll(producers.Concat(consumers));
            sw.Stop();

            double totalItems = threadCount * itemsPerThread;
            double opsPerSec = totalItems / sw.Elapsed.TotalSeconds;
            LogResult($"[FixedStack 4P4C] {totalItems:N0} items in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 2_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion

        #region InputStackBuffer Speed

        [TestMethod("SPEED: InputStackBuffer sequential throughput")]
        public async Task Speed_InputStackBuffer_Sequential()
        {
            const int iterations = 10_000;
            var buffer = new InputStackBuffer(100, 1_000_000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                await buffer.Push(new FakeArray(100), default);
                var handle = await buffer.Pop(default);
                handle.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[InputStackBuffer Sequential] {iterations} push+pop in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 5_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: InputStackBuffer concurrent throughput")]
        public async Task Speed_InputStackBuffer_Concurrent()
        {
            const int totalItems = 20_000;
            var buffer = new InputStackBuffer(50, 5_000_000);
            var cts = new CancellationTokenSource(30000);

            var sw = Stopwatch.StartNew();

            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                    await buffer.Push(new FakeArray(100), cts.Token);
            });

            var consumer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                {
                    var handle = await buffer.Pop(cts.Token);
                    handle.Dispose();
                }
            });

            await Task.WhenAll(producer, consumer);
            sw.Stop();

            double opsPerSec = totalItems / sw.Elapsed.TotalSeconds;
            LogResult($"[InputStackBuffer Concurrent] {totalItems} items in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 2_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion

        #region NGCArray Speed

        [TestMethod("SPEED: NGCArray alloc+dispose throughput (no reuse)")]
        public void Speed_NGCArray_AllocDispose_Cold()
        {
            // РќРµ РїРµСЂРµРёСЃРїРѕР»СЊР·СѓРµРј вЂ” РєР°Р¶РґС‹Р№ СЂР°Р· РЅРѕРІС‹Р№ СЂР°Р·РјРµСЂ
            NGCArray.ArrayLifetime = TimeSpan.FromMilliseconds(1);
            Thread.Sleep(50); // РґР°РґРёРј cleaner'Сѓ РїРѕС‡РёСЃС‚РёС‚СЊ

            const int iterations = 50_000;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var arr = new NGCArray(64 + (i % 1000)); // СѓРЅРёРєР°Р»СЊРЅС‹Рµ СЂР°Р·РјРµСЂС‹
                arr.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[NGCArray Cold] {iterations} alloc+dispose in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 50_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: NGCArray alloc+dispose throughput (pool reuse)")]
        public void Speed_NGCArray_AllocDispose_Hot()
        {
            // РћРґРёРЅ Рё С‚РѕС‚ Р¶Рµ СЂР°Р·РјРµСЂ вЂ” РјР°РєСЃРёРјР°Р»СЊРЅРѕРµ РїРµСЂРµРёСЃРїРѕР»СЊР·РѕРІР°РЅРёРµ
            NGCArray.ArrayLifetime = TimeSpan.FromSeconds(30);

            const int iterations = 100_000;

            // РџСЂРѕРіСЂРµРІ
            var warm = new NGCArray(512);
            warm.Dispose();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var arr = new NGCArray(512);
                arr.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[NGCArray Hot] {iterations} alloc+dispose in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 100_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: NGCArray concurrent alloc+dispose")]
        public async Task Speed_NGCArray_ConcurrentAllocDispose()
        {
            NGCArray.ArrayLifetime = TimeSpan.FromSeconds(30);
            const int threads = 4;
            const int iterPerThread = 25_000;

            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, threads).Select(t =>
                Task.Run(() =>
                {
                    for (int i = 0; i < iterPerThread; i++)
                    {
                        var arr = new NGCArray(256 + (i % 10) * 32);
                        arr.Dispose();
                    }
                })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            double totalOps = threads * iterPerThread;
            double opsPerSec = totalOps / sw.Elapsed.TotalSeconds;
            LogResult($"[NGCArray Concurrent 4T] {totalOps:N0} ops in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 50_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion

        #region RPC Speed

        [TestMethod("SPEED: RPC register+lookup+remove throughput")]
        public void Speed_RPC_RegisterLookupRemove()
        {
            const int iterations = 50_000;
            var rpc = new RPC();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var indicator = new Indicator.Func($"Speed_{i}");
                var handle = rpc.RegisterMethod(() => { }, indicator);
                var found = rpc.TryGetRegisteredMethod(indicator.ID);
                handle.Remove();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[RPC Register+Lookup+Remove] {iterations} cycles in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 10_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: RPC lookup-only throughput (hot path)")]
        public void Speed_RPC_LookupOnly()
        {
            const int methodCount = 100;
            const int lookups = 500_000;
            var rpc = new RPC();

            var indicators = new Indicator.Func[methodCount];
            for (int i = 0; i < methodCount; i++)
            {
                indicators[i] = new Indicator.Func($"HotLookup_{i}");
                rpc.RegisterMethod(() => { }, indicators[i]);
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < lookups; i++)
            {
                rpc.TryGetRegisteredMethod(indicators[i % methodCount].ID);
            }
            sw.Stop();

            double opsPerSec = lookups / sw.Elapsed.TotalSeconds;
            LogResult($"[RPC Lookup Only] {lookups} lookups in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 100_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        [TestMethod("SPEED: RPC concurrent register+lookup")]
        public async Task Speed_RPC_ConcurrentRegisterLookup()
        {
            const int threads = 4;
            const int iterPerThread = 10_000;
            var rpc = new RPC();

            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, threads).Select(t =>
                Task.Run(() =>
                {
                    for (int i = 0; i < iterPerThread; i++)
                    {
                        var ind = new Indicator.Func($"ConcSpeed_{t}_{i}");
                        var handle = rpc.RegisterMethod(() => { }, ind);
                        rpc.TryGetRegisteredMethod(ind.ID);
                        handle.Remove();
                    }
                })).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            double totalOps = threads * iterPerThread;
            double opsPerSec = totalOps / sw.Elapsed.TotalSeconds;
            LogResult($"[RPC Concurrent 4T] {totalOps:N0} cycles in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 5_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion

        #region Indicator Speed

        [TestMethod("SPEED: Indicator.Func creation + ID hashing")]
        public void Speed_Indicator_Creation()
        {
            const int iterations = 200_000;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var ind = new Indicator.Func($"Perf_{i}");
                _ = ind.ID;
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[Indicator Creation] {iterations} in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 100_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion

        #region Deterministic Hash Speed

        [TestMethod("SPEED: Deterministic.GetDeterministicHashCode")]
        public void Speed_DeterministicHash()
        {
            const int iterations = 1_000_000;
            string[] testStrings = Enumerable.Range(0, 100).Select(i => $"TestString_{i}").ToArray();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                testStrings[i % 100].DeterministicGetHashCode();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            LogResult($"[Deterministic Hash] {iterations} hashes in {sw.ElapsedMilliseconds}ms = {opsPerSec:N0} ops/sec");

            Assert.IsTrue(opsPerSec > 1_000_000, $"Throughput СЃР»РёС€РєРѕРј РЅРёР·РєРёР№: {opsPerSec:N0} ops/sec");
        }

        #endregion
    }
}
