namespace EMI.Test
{
    [TestClass]
    public class Test_FixedStack
    {
        [TestMethod("Push и Pop работают FIFO (Channel<T>)")]
        public async Task PushPop_FIFO()
        {
            var stack = new FixedStack<int>(10, TimeSpan.FromSeconds(5));
            await stack.Push(1, default);
            await stack.Push(2, default);
            await stack.Push(3, default);

            Assert.AreEqual(3, stack.Count);
            Assert.AreEqual(1, await stack.Pop(default));
            Assert.AreEqual(2, await stack.Pop(default));
            Assert.AreEqual(3, await stack.Pop(default));
            Assert.AreEqual(0, stack.Count);
        }

        [TestMethod("Size возвращает правильный размер")]
        public void Size_ReturnsCorrectValue()
        {
            var stack = new FixedStack<string>(7, TimeSpan.FromSeconds(1));
            Assert.AreEqual(7, stack.Size);
            Assert.AreEqual(0, stack.Count);
        }

        [TestMethod("Pop на пустом стеке с отменой возвращает default")]
        public async Task Pop_EmptyStack_Cancelled_ReturnsDefault()
        {
            // Pop на пустом стеке — CancellationToken для выхода
            // (MaxWaitTime одна не гарантирует возврат из-за goto reWait без проверки таймаута)
            var stack = new FixedStack<int>(5, TimeSpan.FromMilliseconds(100));
            var cts = new CancellationTokenSource(200);
            int result = await stack.Pop(cts.Token);
            Assert.AreEqual(default(int), result);
        }

        [TestMethod("Pop на пустом стеке блокируется и разблокируется Push-ем")]
        public async Task Pop_BlocksUntilPush()
        {
            var stack = new FixedStack<int>(5, TimeSpan.FromSeconds(5));
            var cts = new CancellationTokenSource(3000);

            var popTask = Task.Run(async () => await stack.Pop(cts.Token));

            // Даём Pop немного повисеть
            await Task.Delay(50);
            Assert.IsFalse(popTask.IsCompleted, "Pop должен блокироваться на пустом стеке");

            await stack.Push(42, default);
            int result = await popTask;
            Assert.AreEqual(42, result);
        }

        [TestMethod("Push на полном стеке блокируется и разблокируется Pop-ом")]
        public async Task Push_BlocksWhenFull_UnblocksOnPop()
        {
            var stack = new FixedStack<int>(2, TimeSpan.FromSeconds(5));
            await stack.Push(1, default);
            await stack.Push(2, default);
            Assert.AreEqual(2, stack.Count);

            var cts = new CancellationTokenSource(3000);
            var pushTask = Task.Run(async () => await stack.Push(3, cts.Token));

            await Task.Delay(50);
            Assert.IsFalse(pushTask.IsCompleted, "Push должен блокироваться на полном стеке");

            var popped = await stack.Pop(default);
            await pushTask; // теперь Push должен завершиться

            Assert.AreEqual(2, stack.Count);
        }

        [TestMethod("CancellationToken прерывает Push")]
        public async Task Push_CancellationToken_Honored()
        {
            var stack = new FixedStack<int>(1, TimeSpan.FromSeconds(5));
            await stack.Push(1, default); // заполнили

            var cts = new CancellationTokenSource(100);
            await stack.Push(99, cts.Token); // должно просто вернуться после отмены

            // После отмены стек не должен расти
            Assert.AreEqual(1, stack.Count);
        }

        [TestMethod("CancellationToken прерывает Pop")]
        public async Task Pop_CancellationToken_Honored()
        {
            var stack = new FixedStack<int>(5, TimeSpan.FromSeconds(5));
            var cts = new CancellationTokenSource(100);
            int result = await stack.Pop(cts.Token);
            Assert.AreEqual(default(int), result);
        }

        [TestMethod("Конкурентные Push и Pop")]
        public async Task ConcurrentPushPop()
        {
            var stack = new FixedStack<int>(100, TimeSpan.FromSeconds(10));
            int count = 1000;
            var cts = new CancellationTokenSource(10000);

            var pushTask = Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                    await stack.Push(i, cts.Token);
            });

            var results = new List<int>();
            var popTask = Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                    results.Add(await stack.Pop(cts.Token));
            });

            await Task.WhenAll(pushTask, popTask);

            Assert.AreEqual(count, results.Count);
            Assert.AreEqual(0, stack.Count);
        }

        [TestMethod("Ссылочные типы работают корректно")]
        public async Task ReferenceTypes_Work()
        {
            var stack = new FixedStack<string>(5, TimeSpan.FromSeconds(1));
            await stack.Push("hello", default);
            await stack.Push(null, default);

            Assert.AreEqual("hello", await stack.Pop(default));
            Assert.IsNull(await stack.Pop(default));
        }
    }
}
