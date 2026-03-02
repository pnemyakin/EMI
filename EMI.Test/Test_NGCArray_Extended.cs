namespace EMI.Test
{
    [TestClass]
    [DoNotParallelize]
    public class Test_NGCArray_Extended
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ArrayLifetime = new TimeSpan(0, 0, 0, 0, 50);
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

        [TestMethod("Dispose возвращает массив в пул")]
        public void Dispose_ReturnsToPool()
        {
            var arr = new NGCArray(500);
            byte[] bytes = arr.Bytes;
            arr.Dispose();

            // Массив должен быть переиспользован
            var arr2 = new NGCArray(500);
            Assert.AreSame(bytes, arr2.Bytes);
            arr2.Dispose();
        }

        [TestMethod("Dispose дважды не вызывает ошибку")]
        public void Dispose_Twice_Safe()
        {
            var arr = new NGCArray(100);
            arr.Dispose();
            arr.Dispose(); // второй вызов — Bytes уже null, не должно упасть
        }

        [TestMethod("Best-fit: выбирается подходящий массив из пула")]
        public void BestFit_SelectsSuitableArray()
        {
            // Для теста best-fit нужна изоляция от предыдущих тестов
            // Ждём чтобы Cleaner очистил старые массивы
            Thread.Sleep(100);

            // Используем уникальные размеры которые не встречаются в других тестах
            var a1 = new NGCArray(1111);
            var a2 = new NGCArray(2222);
            var a3 = new NGCArray(3333);
            byte[] b2 = a2.Bytes;
            a1.Dispose();
            a2.Dispose();
            a3.Dispose();

            // Запрашиваем 1500 — best-fit выберет массив >= 1500 и < 15000
            // Из пула: 1111 (< 1500, не подходит), 2222 (>= 1500, подходит), 3333 (>= 1500, подходит но больше)
            // Должен выбрать 2222 (наименьший подходящий)
            var result = new NGCArray(1500);
            Assert.AreSame(b2, result.Bytes, 
                $"Ожидался массив размера 2222, получен {result.Bytes.Length}");
            result.Dispose();
        }

        [TestMethod("Слишком большие массивы не переиспользуются (10x лимит)")]
        public void TooLargeArray_NotReused()
        {
            var big = new NGCArray(10000);
            byte[] bigBytes = big.Bytes;
            big.Dispose();

            // Запрашиваем 100 — массив 10000 слишком велик (10000 >= 100 * 10)
            var small = new NGCArray(100);
            Assert.AreNotSame(bigBytes, small.Bytes);
            small.Dispose();
        }

        [TestMethod("Offset можно устанавливать")]
        public void Offset_CanBeSet()
        {
            var arr = new NGCArray(100);
            arr.Offset = 50;
            Assert.AreEqual(50, arr.Offset);
            arr.Dispose();
        }

        [TestMethod("Cleaner удаляет истёкшие массивы")]
        public async Task Cleaner_RemovesExpiredArrays()
        {
            NGCArray.ArrayLifetime = new TimeSpan(0, 0, 0, 0, 50);

            var arr = new NGCArray(999);
            byte[] bytes = arr.Bytes;
            arr.Dispose();

            // Ждём дольше чем ArrayLifetime + запас на Cleaner
            await Task.Delay(300);

            // Теперь массив должен быть очищен — новый запрос не найдёт его
            var arr2 = new NGCArray(999);
            Assert.AreNotSame(bytes, arr2.Bytes);
            arr2.Dispose();
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
