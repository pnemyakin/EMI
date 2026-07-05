namespace EMI.Test
{
    [TestClass]
    [DoNotParallelize]
    public class Test_NGCArray_Extended
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ClearPool();
        }

        [TestMethod("Конструктор создаёт массив нужного размера")]
        public void Constructor_CreatesCorrectSize()
        {
            var arr = new NGCArray(256);
            Assert.AreEqual(256, arr.Length);
            Assert.IsNotNull(arr.Bytes);
            Assert.IsTrue(arr.Bytes.Length >= 256);
            Assert.AreEqual(0, arr.Offset);
            arr.Dispose();
        }

        [TestMethod("Dispose дважды не вызывает ошибку")]
        public void Dispose_Twice_Safe()
        {
            var arr = new NGCArray(100);
            arr.Dispose();
            arr.Dispose(); // второй вызов — Bytes уже null, не должно упасть
        }

        [TestMethod("Offset можно устанавливать")]
        public void Offset_CanBeSet()
        {
            var arr = new NGCArray(100);
            arr.Offset = 50;
            Assert.AreEqual(50, arr.Offset);
            arr.Dispose();
        }

        [TestMethod("Множественное создание и освобождение не ломается")]
        public void ManyAllocationsAndDisposals()
        {
            for (int i = 0; i < 100; i++)
            {
                var arr = new NGCArray(i + 1);
                Assert.AreEqual(i + 1, arr.Length);
                arr.Dispose();
            }
        }
    }
}
