namespace EMI.Test
{
    [TestClass]
    public class Test_EasyArray
    {
        [TestMethod("Конструктор с размером создаёт массив")]
        public void Constructor_WithSize_CreatesArray()
        {
            var arr = new EasyArray(100);
            Assert.AreEqual(100, arr.Length);
            Assert.IsNotNull(arr.Bytes);
            Assert.AreEqual(100, arr.Bytes.Length);
            Assert.AreEqual(0, arr.Offset);
        }

        [TestMethod("Конструктор с нулевым размером создаёт пустой массив")]
        public void Constructor_ZeroSize_NullBytes()
        {
            var arr = new EasyArray(0);
            Assert.AreEqual(0, arr.Length);
            Assert.IsNull(arr.Bytes);
        }

        [TestMethod("Конструктор из существующего массива оборачивает его")]
        public void Constructor_FromArray_WrapsIt()
        {
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            var arr = new EasyArray(data);
            Assert.AreEqual(5, arr.Length);
            Assert.AreSame(data, arr.Bytes);
            Assert.AreEqual(0, arr.Offset);
        }

        [TestMethod("Offset можно изменять")]
        public void Offset_CanBeSet()
        {
            var arr = new EasyArray(100);
            arr.Offset = 42;
            Assert.AreEqual(42, arr.Offset);
        }

        [TestMethod("Dispose не вызывает исключений")]
        public void Dispose_DoesNotThrow()
        {
            var arr = new EasyArray(100);
            arr.Dispose(); // no-op, не должна падать
        }

        [TestMethod("Данные в массиве сохраняются")]
        public void Data_IsPersisted()
        {
            var arr = new EasyArray(4);
            arr.Bytes[0] = 0xAA;
            arr.Bytes[3] = 0xBB;
            Assert.AreEqual(0xAA, arr.Bytes[0]);
            Assert.AreEqual(0xBB, arr.Bytes[3]);
        }
    }

    [TestClass]
    public class Test_INGCArrayUtils
    {
        [TestMethod("EmptyArray действительно пуст")]
        public void EmptyArray_IsEmpty()
        {
            Assert.IsTrue(INGCArrayUtils.IsEmpty(INGCArrayUtils.EmptyArray));
        }

        [TestMethod("Непустой массив не является Empty")]
        public void NonEmptyArray_IsNotEmpty()
        {
            var arr = new EasyArray(10);
            Assert.IsFalse(arr.IsEmpty());
        }

        [TestMethod("Массив нулевого размера является Empty")]
        public void ZeroSizeArray_IsEmpty()
        {
            var arr = new EasyArray(0);
            Assert.IsTrue(arr.IsEmpty());
        }

        [TestMethod("FakeArray не пуст")]
        public void FakeArray_IsNotEmpty()
        {
            var arr = new FakeArray(5);
            Assert.IsFalse(arr.IsEmpty());
        }
    }
}
