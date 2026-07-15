using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMI;

namespace EMI.Test
{
    /// <summary>
    /// Юнит-тесты стратегий исполнения RPC (IRpcDispatcher).
    /// </summary>
    [TestClass]
    public class Test_Dispatcher
    {
        [TestMethod("Inline: исполняет синхронно на текущем потоке")]
        public void Inline_RunsSynchronously()
        {
            int tid = Thread.CurrentThread.ManagedThreadId;
            int ranOn = -1;
            bool ran = false;
            InlineDispatcher.Instance.Post(() => { ran = true; ranOn = Thread.CurrentThread.ManagedThreadId; });
            Assert.IsTrue(ran, "Inline должен выполнить вызов немедленно");
            Assert.AreEqual(tid, ranOn, "тот же поток");
        }

        [TestMethod("Pump: ничего не исполняется до Pump()")]
        public void Pump_DefersUntilPump()
        {
            var pump = new PumpDispatcher();
            bool ran = false;
            pump.Post(() => ran = true);
            Assert.IsFalse(ran, "до Pump() вызов не должен исполниться");
            Assert.AreEqual(1, pump.PendingCount);

            int done = pump.Pump();
            Assert.IsTrue(ran, "после Pump() вызов исполнен");
            Assert.AreEqual(1, done);
            Assert.AreEqual(0, pump.PendingCount);
        }

        [TestMethod("Pump: исполняет в потоке, вызвавшем Pump()")]
        public void Pump_RunsOnPumpingThread()
        {
            var pump = new PumpDispatcher();
            int posterTid = -1, pumpTid = -1;

            // Постим из фонового потока
            var t = new Thread(() => { posterTid = Thread.CurrentThread.ManagedThreadId; pump.Post(() => { }); });
            t.Start(); t.Join();

            pump.Post(() => pumpTid = Thread.CurrentThread.ManagedThreadId);
            pump.Pump();
            Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, pumpTid, "вызов исполнен в потоке Pump()");
            Assert.AreNotEqual(posterTid, Thread.CurrentThread.ManagedThreadId, "постили из другого потока");
        }

        [TestMethod("Pump: строгий FIFO-порядок")]
        public void Pump_FifoOrder()
        {
            var pump = new PumpDispatcher();
            var order = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                int n = i;
                pump.Post(() => order.Add(n));
            }
            pump.Pump();
            CollectionAssert.AreEqual(new List<int>(Range(0, 100)), order);
        }

        [TestMethod("Pump: не зацикливается на вызовах, добавленных во время Pump()")]
        public void Pump_ReentrantPostGoesToNextPump()
        {
            var pump = new PumpDispatcher();
            int ranFirstBatch = 0;
            pump.Post(() => { ranFirstBatch++; pump.Post(() => ranFirstBatch++); });

            int done = pump.Pump();
            Assert.AreEqual(1, done, "первый Pump исполняет только то, что было в очереди на его старте");
            Assert.AreEqual(1, ranFirstBatch);
            Assert.AreEqual(1, pump.PendingCount, "добавленный во время Pump ждёт следующего");

            pump.Pump();
            Assert.AreEqual(2, ranFirstBatch);
        }

        [TestMethod("Pump(max): ограничивает бюджет за заход")]
        public void Pump_BudgetLimit()
        {
            var pump = new PumpDispatcher();
            for (int i = 0; i < 10; i++) pump.Post(() => { });
            Assert.AreEqual(3, pump.Pump(3));
            Assert.AreEqual(7, pump.PendingCount);
        }

        [TestMethod("Pump: DropNewest отбрасывает новый при переполнении")]
        public void Pump_OverflowDropNewest()
        {
            var pump = new PumpDispatcher(maxQueueDepth: 2, overflowPolicy: PumpOverflowPolicy.DropNewest);
            var order = new List<int>();
            pump.Post(() => order.Add(1));
            pump.Post(() => order.Add(2));
            pump.Post(() => order.Add(3)); // отброшен
            pump.Pump();
            CollectionAssert.AreEqual(new List<int> { 1, 2 }, order);
        }

        [TestMethod("Pump: DropOldest освобождает место под новый")]
        public void Pump_OverflowDropOldest()
        {
            var pump = new PumpDispatcher(maxQueueDepth: 2, overflowPolicy: PumpOverflowPolicy.DropOldest);
            var order = new List<int>();
            pump.Post(() => order.Add(1)); // вытеснен
            pump.Post(() => order.Add(2));
            pump.Post(() => order.Add(3));
            pump.Pump();
            CollectionAssert.AreEqual(new List<int> { 2, 3 }, order);
        }

        [TestMethod("Pump: исключение в хендлере не роняет Pump и уходит в OnHandlerError")]
        public void Pump_HandlerExceptionIsolated()
        {
            var pump = new PumpDispatcher();
            Exception caught = null;
            pump.OnHandlerError += e => caught = e;

            int after = 0;
            pump.Post(() => throw new InvalidOperationException("boom"));
            pump.Post(() => after++);

            int done = pump.Pump();
            Assert.AreEqual(2, done, "оба вызова обработаны, несмотря на исключение в первом");
            Assert.AreEqual(1, after);
            Assert.IsInstanceOfType(caught, typeof(InvalidOperationException));
        }

        [TestMethod("Pump: Clear отбрасывает без исполнения")]
        public void Pump_Clear()
        {
            var pump = new PumpDispatcher();
            bool ran = false;
            pump.Post(() => ran = true);
            pump.Clear();
            Assert.AreEqual(0, pump.PendingCount);
            Assert.AreEqual(0, pump.Pump());
            Assert.IsFalse(ran);
        }

        [TestMethod("ThreadPool: исполняет не на текущем потоке")]
        [Timeout(5000)]
        public void ThreadPool_RunsOffThread()
        {
            int tid = Thread.CurrentThread.ManagedThreadId;
            int ranOn = -1;
            using var done = new ManualResetEventSlim(false);
            ThreadPoolDispatcher.Instance.Post(() => { ranOn = Thread.CurrentThread.ManagedThreadId; done.Set(); });
            Assert.IsTrue(done.Wait(2000), "вызов должен выполниться на пуле");
            Assert.AreNotEqual(tid, ranOn);
        }

        [TestMethod("SyncContext: постит в переданный контекст")]
        public void SyncContext_PostsToContext()
        {
            var ctx = new RecordingContext();
            var disp = new SynchronizationContextDispatcher(ctx);
            bool ran = false;
            disp.Post(() => ran = true);
            Assert.AreEqual(1, ctx.PostCount, "должен вызвать Post контекста");
            Assert.IsTrue(ran, "RecordingContext исполняет вызов синхронно");
        }

        [TestMethod("SyncContext: без контекста на потоке — исключение")]
        public void SyncContext_NoContextThrows()
        {
            SynchronizationContext.SetSynchronizationContext(null);
            Assert.ThrowsException<InvalidOperationException>(() => new SynchronizationContextDispatcher());
        }

        private static IEnumerable<int> Range(int start, int count)
        {
            for (int i = 0; i < count; i++) yield return start + i;
        }

        private sealed class RecordingContext : SynchronizationContext
        {
            public int PostCount;
            public override void Post(SendOrPostCallback d, object state)
            {
                PostCount++;
                d(state); // исполняем сразу — для детерминизма теста
            }
        }
    }
}
