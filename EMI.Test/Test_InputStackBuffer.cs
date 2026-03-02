namespace EMI.Test
{
    [TestClass]
    public class Test_InputStackBuffer
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ArrayLifetime = new TimeSpan(0, 0, 0, 0, 50);
        }


        [TestMethod("�������� ���������� ��� ������������ ������� 1")]
        public void Test1()
        {
            try
            {
                CancellationTokenSource source = new(1000);
                Task.Run(async () =>
                {
                    InputStackBuffer buffer = new(5, 15);
                    await buffer.Push(new FakeArray(6), default);
                    await buffer.Push(new FakeArray(15), default);
                    var handle = await buffer.Pop(default);
                    await buffer.Push(new FakeArray(100), default);
                    Assert.IsTrue(false);
                }).Wait(source.Token);
            }
            catch
            {
                Assert.IsTrue(true);
            }
        }


        [TestMethod("�������� ���������� ��� ������������ ������� 2")]
        public void Test2()
        {
            CancellationTokenSource source = new(1000);
            Task.Run(async () =>
            {
                InputStackBuffer buffer = new(5, 15);
                await buffer.Push(new FakeArray(12), default);
                await buffer.Push(new FakeArray(6), default);
                var handle = await buffer.Pop(default);
                handle.Dispose();
                await buffer.Push(new FakeArray(100), default);
                Assert.IsTrue(true);
            }).Wait(source.Token);
        }

        [TestMethod("�������� ���������� ������� �����")]
        public void Test3()
        {
            CancellationTokenSource source = new(1000);
            InputStackBuffer buffer = new(5, 15);
            var task = Task.Run(async () =>
            {
                var handle = await buffer.Pop(default);
                Assert.IsTrue(true);
            });
            _ = buffer.Push(new FakeArray(6), default);
            task.Wait(source.Token);
        }

        [TestMethod("�������� ���������� ������������� �����")]
        public void Test4()
        {
            try
            {
                CancellationTokenSource source = new(1000);
                InputStackBuffer buffer = new(2, 15);
                var task = Task.Run(async () =>
                {
                    await buffer.Push(new FakeArray(1), default);
                    await buffer.Push(new FakeArray(1), default);
                    await buffer.Push(new FakeArray(1), default);
                    Assert.IsTrue(false);
                });
                task.Wait(source.Token);
            }
            catch
            {
                Assert.IsTrue(true);
            }
        }
    }
}