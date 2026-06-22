using EMI.Indicators;

namespace EMI.Test
{
    [TestClass]
    public class Test_RCWaitHandle
    {
        [TestMethod("Семафор начинается в заблокированном состоянии")]
        public void Semaphore_StartsLocked()
        {
            var indicator = new Indicator.Func("WaitTest");
            var handle = new RCWaitHandle(indicator, 1);

            // WaitAsync с нулевым таймаутом — должен не пройти (семафор = 0)
            bool entered = handle.Semaphore.Wait(0);
            Assert.IsFalse(entered);
        }

        [TestMethod("Release разблокирует WaitAsync")]
        public async Task Release_UnlocksWait()
        {
            var indicator = new Indicator.Func("ReleaseTest");
            var handle = new RCWaitHandle(indicator, 2);

            // Освобождаем семафор
            handle.Semaphore.Release();

            // Теперь WaitAsync должен пройти мгновенно
            var cts = new CancellationTokenSource(1000);
            await handle.Semaphore.WaitAsync(cts.Token);
            // Если дошли сюда — тест прошёл
        }

        [TestMethod("Indicator сохраняется в хендле")]
        public void Indicator_IsStored()
        {
            var indicator = new Indicator.Func("StoredTest");
            var handle = new RCWaitHandle(indicator, 3);
            Assert.AreSame(indicator, handle.Indicator);
        }

        [TestMethod("WaitAsync с CancellationToken прерывается")]
        public async Task WaitAsync_CancellationToken_Works()
        {
            var indicator = new Indicator.Func("CancelWaitTest");
            var handle = new RCWaitHandle(indicator, 4);

            var cts = new CancellationTokenSource(50);
            try
            {
                await handle.Semaphore.WaitAsync(cts.Token);
                Assert.Fail("Должен был выбросить OperationCanceledException");
            }
            catch (OperationCanceledException)
            {
                // Ожидаемое поведение
            }
        }
    }
}
