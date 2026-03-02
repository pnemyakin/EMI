namespace EMI.Test
{
    using EMI.NGC;
    using EMI.Indicators;

    /// <summary>
    /// Тесты конкурентности — гарантируют корректность при параллельном доступе.
    /// Если заменить spin-wait на Channel или Dictionary на ConcurrentDictionary,
    /// эти тесты должны продолжать проходить.
    /// </summary>
    [TestClass]
    public class Test_Concurrency
    {
        #region FixedStack Concurrency

        [TestMethod("FixedStack: N продюсеров + N консьюмеров, все элементы доставлены")]
        public async Task FixedStack_MultiProducerMultiConsumer_AllDelivered()
        {
            const int stackSize = 50;
            const int itemsPerProducer = 500;
            const int producerCount = 4;
            const int totalItems = producerCount * itemsPerProducer;

            var stack = new FixedStack<int>(stackSize, TimeSpan.FromSeconds(10));
            var cts = new CancellationTokenSource(30000);
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();

            // Запускаем продюсеров
            var producers = new Task[producerCount];
            for (int p = 0; p < producerCount; p++)
            {
                int producerIdx = p;
                producers[p] = Task.Run(async () =>
                {
                    for (int i = 0; i < itemsPerProducer; i++)
                    {
                        await stack.Push(producerIdx * itemsPerProducer + i, cts.Token);
                    }
                });
            }

            // Запускаем консьюмеров (столько же)
            var consumers = new Task[producerCount];
            for (int c = 0; c < producerCount; c++)
            {
                consumers[c] = Task.Run(async () =>
                {
                    for (int i = 0; i < itemsPerProducer; i++)
                    {
                        int val = await stack.Pop(cts.Token);
                        results.Add(val);
                    }
                });
            }

            await Task.WhenAll(producers);
            await Task.WhenAll(consumers);

            Assert.AreEqual(totalItems, results.Count, "Не все элементы доставлены");
            Assert.AreEqual(0, stack.Count, "Стек должен быть пуст");

            // Проверяем что все уникальные ID присутствуют
            var sorted = results.OrderBy(x => x).ToArray();
            for (int i = 0; i < totalItems; i++)
            {
                Assert.AreEqual(i, sorted[i], $"Потерян элемент {i}");
            }
        }

        [TestMethod("FixedStack: переполнение стека — продюсеры ждут, ничего не теряется")]
        public async Task FixedStack_Overflow_ProducersWaitNoLoss()
        {
            const int stackSize = 5;
            const int totalItems = 200;

            var stack = new FixedStack<int>(stackSize, TimeSpan.FromSeconds(10));
            var cts = new CancellationTokenSource(15000);
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();

            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                    await stack.Push(i, cts.Token);
            });

            var consumer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                    results.Add(await stack.Pop(cts.Token));
            });

            await Task.WhenAll(producer, consumer);
            Assert.AreEqual(totalItems, results.Count);
        }

        [TestMethod("FixedStack: CancellationToken корректно останавливает все потоки")]
        public async Task FixedStack_Cancellation_StopsAll()
        {
            var stack = new FixedStack<int>(2, TimeSpan.FromSeconds(30));
            var cts = new CancellationTokenSource(200);

            // Запускаем Pop на пустом стеке — зависнет до отмены
            var popTask = Task.Run(async () => await stack.Pop(cts.Token));
            // Запускаем Push на полном стеке
            await stack.Push(1, CancellationToken.None);
            await stack.Push(2, CancellationToken.None);
            var pushTask = Task.Run(async () => await stack.Push(3, cts.Token));

            await Task.WhenAll(popTask, pushTask);
            // Не зависло — тест прошёл
        }

        #endregion

        #region InputStackBuffer Concurrency

        [TestMethod("InputStackBuffer: конкурентные Push/Pop не теряют данные")]
        public async Task InputStackBuffer_ConcurrentPushPop_NoLoss()
        {
            const int itemCount = 100;
            const int bufferSize = 20;
            const int capacity = 50000;

            var buffer = new InputStackBuffer(bufferSize, capacity);
            var cts = new CancellationTokenSource(15000);
            var results = new System.Collections.Concurrent.ConcurrentBag<int>();

            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < itemCount; i++)
                {
                    var fakeArray = new FakeArray(100 + i);
                    await buffer.Push(fakeArray, cts.Token);
                }
            });

            var consumer = Task.Run(async () =>
            {
                for (int i = 0; i < itemCount; i++)
                {
                    var handle = await buffer.Pop(cts.Token);
                    if (handle.Buffer != null)
                        results.Add(handle.Buffer.Bytes.Length);
                    handle.Dispose();
                }
            });

            await Task.WhenAll(producer, consumer);
            Assert.AreEqual(itemCount, results.Count);
            Assert.AreEqual(0, buffer.OccupiedCapacity, "Ёмкость должна быть полностью освобождена");
            Assert.AreEqual(0, buffer.Count, "Стек должен быть пуст");
        }

        [TestMethod("InputStackBuffer: capacity-лимит работает при конкурентном доступе")]
        public async Task InputStackBuffer_CapacityLimit_ConcurrentAccess()
        {
            // Лимит 500 байт, элементы по 200 — максимум 2 одновременно
            var buffer = new InputStackBuffer(10, 500);
            var cts = new CancellationTokenSource(10000);
            int maxObservedCapacity = 0;
            object lockObj = new object();

            const int totalItems = 50;

            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < totalItems; i++)
                {
                    await buffer.Push(new FakeArray(200), cts.Token);
                    lock (lockObj)
                    {
                        int cur = buffer.OccupiedCapacity;
                        if (cur > maxObservedCapacity)
                            maxObservedCapacity = cur;
                    }
                }
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

            // capacity может кратковременно превысить лимит (by design — push добавляет если место есть, 
            // а потом ещё один элемент может пролезть), но не должна быть дикой
            Assert.IsTrue(maxObservedCapacity <= 500 + 200,
                $"Capacity превысил лимит+элемент: {maxObservedCapacity}");
        }

        #endregion

        #region NGCArray Concurrency

        [TestMethod("NGCArray: конкурентные alloc/dispose не повреждают пул")]
        public async Task NGCArray_ConcurrentAllocDispose_NoCrash()
        {
            NGCArray.ArrayLifetime = TimeSpan.FromSeconds(5); // не мешаем cleaner'у

            const int threads = 4;
            const int iterations = 200;

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var arr = new NGCArray(64 + (i % 10) * 32);
                        Assert.IsNotNull(arr.Bytes);
                        Assert.IsTrue(arr.Bytes.Length >= 64);
                        arr.Dispose();
                    }
                });
            }

            await Task.WhenAll(tasks);
            // Не упало — пул работает корректно при конкурентном доступе
        }

        [TestMethod("NGCArray: конкурентный Dispose не дублирует в пуле")]
        public async Task NGCArray_ConcurrentDispose_NoDuplicates()
        {
            NGCArray.ArrayLifetime = TimeSpan.FromSeconds(5);

            const int count = 50;
            var arrays = new NGCArray[count];
            var allBytes = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                arrays[i] = new NGCArray(90000 + i * 100); // очень уникальные размеры  
                allBytes.Add(arrays[i].Bytes);
            }

            // Параллельный Dispose
            await Task.WhenAll(arrays.Select(a => Task.Run(() => a.Dispose())));

            // Аллоцируем заново — каждый должен быть уникальным экземпляром
            var reused = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < count; i++)
            {
                var arr = new NGCArray(90000 + i * 100);
                reused.Add(arr.Bytes);
                arr.Dispose();
            }

            // Не должно быть дублей
            Assert.AreEqual(count, reused.Count, "Есть дублированные массивы в пуле");
        }

        #endregion

        #region RPC Concurrency

        [TestMethod("RPC: конкурентная регистрация/удаление не ломает словарь")]
        public async Task RPC_ConcurrentRegisterRemove_NoCrash()
        {
            var rpc = new RPC();
            const int threads = 4;
            const int iterations = 100;

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                int threadIdx = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var indicator = new Indicator.Func($"Concurrent_{threadIdx}_{i}");
                        var handle = rpc.RegisterMethod(() => { }, indicator);

                        // Иногда поищем
                        var found = rpc.TryGetRegisteredMethod(indicator.ID);
                        Assert.IsNotNull(found, $"Метод {threadIdx}_{i} не найден сразу после регистрации");

                        handle.Remove();

                        var afterRemove = rpc.TryGetRegisteredMethod(indicator.ID);
                        Assert.IsNull(afterRemove, $"Метод {threadIdx}_{i} найден после удаления");
                    }
                });
            }

            await Task.WhenAll(tasks);
        }

        [TestMethod("RPC: конкурентный lookup при регистрации — нет исключений")]
        public async Task RPC_ConcurrentLookupDuringMutation_NoException()
        {
            var rpc = new RPC();
            var cts = new CancellationTokenSource(3000);

            // Регистрируем базовые методы
            var indicators = new Indicator.Func[50];
            for (int i = 0; i < 50; i++)
            {
                indicators[i] = new Indicator.Func($"Lookup_{i}");
                rpc.RegisterMethod(() => { }, indicators[i]);
            }

            // Один поток постоянно ищет
            var reader = Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    foreach (var ind in indicators)
                    {
                        rpc.TryGetRegisteredMethod(ind.ID);
                    }
                }
            });

            // Другой поток добавляет/удаляет
            var writer = Task.Run(() =>
            {
                for (int i = 0; i < 200 && !cts.IsCancellationRequested; i++)
                {
                    var ind = new Indicator.Func($"Writer_{i}");
                    var handle = rpc.RegisterMethod(() => { }, ind);
                    handle.Remove();
                }
                cts.Cancel();
            });

            await Task.WhenAll(reader, writer);
            // Если не упало — concurrent access под lock работает
        }

        #endregion
    }
}
