using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EMI.Test
{
    [TestClass]
    [DoNotParallelize]
    public class Test_NGCArray
    {
        [TestInitialize]
        public void Init()
        {
            NGCArray.ArrayLifetime = new TimeSpan(0, 0, 0, 0, 50);
            NGCArray.ClearPool();
        }

        [TestMethod("Проверка реиспользования массива")]
        public void Test1()
        {
            // Используем уникальные размеры чтобы избежать влияния других тестов
            var arr1 = new NGCArray(10001);
            new NGCArray(50001).Dispose();
            var arr2 = new NGCArray(500001);
            byte[] a1 = arr1.Bytes;
            byte[] a2 = arr2.Bytes;
            arr1.Dispose();
            arr2.Dispose();
            var arrT = new NGCArray(10001);
            Assert.AreEqual(a1, arrT.Bytes);
            arrT.Dispose();
            arrT = new NGCArray(20001);
            Assert.AreNotEqual(a2, arrT.Bytes);
            Assert.AreNotEqual(a1, arrT.Bytes);
            arrT.Dispose();
            arrT = new NGCArray(150001);
            Assert.AreEqual(a2, arrT.Bytes);
            arrT.Dispose();

            Task.Delay(200).Wait();
        }

        [TestMethod("Проверка счётчиков и сборщика неиспользуемых массивов")]
        public void Test2()
        {
            Task.Delay(200).Wait();
            Test1();

            Assert.AreEqual(0, NGCArray.UseArrays);
            Assert.AreEqual(0, NGCArray.TotalUseSize);
            Assert.AreEqual(0, NGCArray.FreeArraysCount);
            Assert.AreEqual(0, NGCArray.TotalFreeArraysSize);
            var arr1 = new NGCArray(10003);
            Assert.AreEqual(1, NGCArray.UseArrays);
            Assert.AreEqual(10003, NGCArray.TotalUseSize);
            var a = arr1.Bytes;
            arr1.Dispose();
            Assert.AreEqual(0, NGCArray.UseArrays);
            Assert.AreEqual(1, NGCArray.FreeArraysCount);
            Assert.AreEqual(10003, NGCArray.TotalFreeArraysSize);
            Assert.AreEqual(0, NGCArray.TotalUseSize);
            Task.Delay(200).Wait();
            Assert.AreEqual(0, NGCArray.UseArrays);
            Assert.AreEqual(0, NGCArray.FreeArraysCount);
            Assert.AreEqual(0, NGCArray.TotalFreeArraysSize);
            Assert.AreEqual(0, NGCArray.TotalUseSize);
            Assert.AreNotEqual(a, new NGCArray(10002).Bytes);

            Task.Delay(200).Wait();
        }
    }
}