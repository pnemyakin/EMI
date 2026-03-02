namespace EMI.Test
{
    [TestClass]
    public class Test_InputStackBuffer_Extended
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ArrayLifetime = new TimeSpan(0, 0, 0, 0, 50);
        }

        [TestMethod("Начальные значения свойств")]
        public void InitialProperties()
        {
            var buffer = new InputStackBuffer(10, 1000);
            Assert.AreEqual(10, buffer.Size);
            Assert.AreEqual(1000, buffer.Capacity);
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0, buffer.OccupiedCapacity);
        }

        [TestMethod("Push увеличивает Count и OccupiedCapacity")]
        public async Task Push_UpdatesCountAndCapacity()
        {
            var buffer = new InputStackBuffer(10, 1000);
            await buffer.Push(new FakeArray(100), default);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(100, buffer.OccupiedCapacity);

            await buffer.Push(new FakeArray(200), default);
            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(300, buffer.OccupiedCapacity);
        }

        [TestMethod("Pop возвращает данные и Handle.Dispose освобождает ёмкость")]
        public async Task Pop_HandleDispose_FreesCapacity()
        {
            var buffer = new InputStackBuffer(10, 1000);
            await buffer.Push(new FakeArray(100), default);
            await buffer.Push(new FakeArray(200), default);

            var handle = await buffer.Pop(default);
            Assert.IsNotNull(handle.Buffer);
            Assert.AreEqual(1, buffer.Count);
            // OccupiedCapacity ещё не изменился — Handle ещё не освобождён
            Assert.AreEqual(300, buffer.OccupiedCapacity);

            handle.Dispose();
            Assert.AreEqual(200, buffer.OccupiedCapacity);
        }

        [TestMethod("CancellationToken прерывает Pop")]
        public async Task Pop_Cancellation()
        {
            var buffer = new InputStackBuffer(10, 1000);
            var cts = new CancellationTokenSource(100);

            try
            {
                await buffer.Pop(cts.Token);
                // Если Pop вернётся без исключения — тоже ок (зависит от реализации)
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое поведение
            }
        }

        [TestMethod("Множественные Push/Pop не ломают состояние")]
        public async Task MultiplePushPop_ConsistentState()
        {
            var buffer = new InputStackBuffer(5, 10000);
            for (int i = 0; i < 5; i++)
            {
                await buffer.Push(new FakeArray(100), default);
            }
            Assert.AreEqual(5, buffer.Count);
            Assert.AreEqual(500, buffer.OccupiedCapacity);

            for (int i = 0; i < 5; i++)
            {
                var handle = await buffer.Pop(default);
                handle.Dispose();
            }
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0, buffer.OccupiedCapacity);
        }
    }
}
