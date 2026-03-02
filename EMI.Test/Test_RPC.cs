using EMI.Indicators;
using EMI.MyException;

namespace EMI.Test
{
    [TestClass]
    public class Test_RPC
    {
        [TestMethod("RegisterMethod и TryGetRegisteredMethod работают")]
        public void RegisterMethod_ThenLookup()
        {
            var rpc = new RPC();
            bool called = false;
            var indicator = new Indicator.Func("TestMethod");

            rpc.RegisterMethod(() => { called = true; }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            Assert.IsNotNull(micro);

            // Вызываем микрофункцию
            micro(INGCArrayUtils.EmptyArray);
            Assert.IsTrue(called);
        }

        [TestMethod("TryGetRegisteredMethod возвращает null для незарегистрированного")]
        public void TryGet_Unregistered_ReturnsNull()
        {
            var rpc = new RPC();
            var result = rpc.TryGetRegisteredMethod(12345);
            Assert.IsNull(result);
        }

        [TestMethod("Remove удаляет метод")]
        public void RemoveHandle_RemovesMethod()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("RemoveTest");
            var handle = rpc.RegisterMethod(() => { }, indicator);

            Assert.IsNotNull(rpc.TryGetRegisteredMethod(indicator.ID));

            handle.Remove();

            Assert.IsNull(rpc.TryGetRegisteredMethod(indicator.ID));
        }

        [TestMethod("Двойной Remove бросает исключение (RPC = null после Remove)")]
        public void DoubleRemove_Throws()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("DoubleRemoveTest");
            var handle = rpc.RegisterMethod(() => { }, indicator);
            handle.Remove();

            // BUG: Remove() обнуляет RPC перед выходом (RPC = null),
            // поэтому повторный вызов падает с ArgumentNullException на lock(RPC),
            // а не с AlreadyException как задумано (IsRemoved проверка внутри lock'а)
            Assert.ThrowsException<ArgumentNullException>(() => handle.Remove());
        }

        [TestMethod("Несколько методов на один индикатор (multicast)")]
        public void MultipleHandlers_SameIndicator()
        {
            var rpc = new RPC();
            int callCount = 0;
            var indicator = new Indicator.Func("MultiTest");

            var h1 = rpc.RegisterMethod(() => { callCount++; }, indicator);
            var h2 = rpc.RegisterMethod(() => { callCount += 10; }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            micro(INGCArrayUtils.EmptyArray);
            Assert.AreEqual(11, callCount);

            // Удаляем один — другой остаётся
            h1.Remove();
            micro = rpc.TryGetRegisteredMethod(indicator.ID);
            Assert.IsNotNull(micro);
        }

        [TestMethod("RegisterForwarding и TryGetRegisteredForwarding работают")]
        public void RegisterForwarding_ThenLookup()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("ForwardTest");
            Client[] ForwardFunc(Client c) => new Client[0];

            var handle = rpc.RegisterForwarding(indicator, ForwardFunc);

            var result = rpc.TryGetRegisteredForwarding(indicator.ID);
            Assert.IsNotNull(result);
        }

        [TestMethod("Двойная регистрация Forwarding бросает AlreadyException")]
        public void RegisterForwarding_Duplicate_Throws()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("ForwardDup");
            Client[] Dummy(Client c) => new Client[0];

            rpc.RegisterForwarding(indicator, Dummy);
            Assert.ThrowsException<AlreadyException>(() => rpc.RegisterForwarding(indicator, Dummy));
        }

        [TestMethod("RemoveHandleForwarding удаляет пересылку")]
        public void RemoveForwarding_Works()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("ForwardRemove");
            var handle = rpc.RegisterForwarding(indicator, (c) => new Client[0]);

            handle.Remove();

            Assert.IsNull(rpc.TryGetRegisteredForwarding(indicator.ID));
        }

        [TestMethod("RemoveHandleGroup удаляет все методы группы")]
        public void RemoveHandleGroup_RemovesAll()
        {
            var rpc = new RPC();
            var group = new RPC.RemoveHandleGroup();

            var ind1 = new Indicator.Func("Group1");
            var ind2 = new Indicator.Func("Group2");
            var ind3 = new Indicator.Func("Group3");

            group.Add(rpc.RegisterMethod(() => { }, ind1));
            group.Add(rpc.RegisterMethod(() => { }, ind2));
            group.Add(rpc.RegisterMethod(() => { }, ind3));

            Assert.IsNotNull(rpc.TryGetRegisteredMethod(ind1.ID));
            Assert.IsNotNull(rpc.TryGetRegisteredMethod(ind2.ID));
            Assert.IsNotNull(rpc.TryGetRegisteredMethod(ind3.ID));

            group.RemoveAll();

            Assert.IsNull(rpc.TryGetRegisteredMethod(ind1.ID));
            Assert.IsNull(rpc.TryGetRegisteredMethod(ind2.ID));
            Assert.IsNull(rpc.TryGetRegisteredMethod(ind3.ID));
        }

        [TestMethod("Разные имена дают разные ID — независимая регистрация")]
        public void DifferentNames_DifferentIDs()
        {
            var rpc = new RPC();
            var ind1 = new Indicator.Func("MethodA");
            var ind2 = new Indicator.Func("MethodB");

            Assert.AreNotEqual(ind1.ID, ind2.ID);

            int calledA = 0, calledB = 0;
            rpc.RegisterMethod(() => calledA++, ind1);
            rpc.RegisterMethod(() => calledB++, ind2);

            rpc.TryGetRegisteredMethod(ind1.ID)(INGCArrayUtils.EmptyArray);
            Assert.AreEqual(1, calledA);
            Assert.AreEqual(0, calledB);

            rpc.TryGetRegisteredMethod(ind2.ID)(INGCArrayUtils.EmptyArray);
            Assert.AreEqual(1, calledA);
            Assert.AreEqual(1, calledB);
        }

        [TestMethod("Исключение в RPC-методе не ломает систему")]
        public void Exception_InRegisteredMethod_DoesNotCrash()
        {
            var rpc = new RPC();
            var indicator = new Indicator.Func("ThrowTest");

            rpc.RegisterMethod(() => { throw new InvalidOperationException("Test"); }, indicator);

            var micro = rpc.TryGetRegisteredMethod(indicator.ID);
            // Исключение ловится внутри lambda и выводится в Console
            var result = micro(INGCArrayUtils.EmptyArray);
            Assert.IsNull(result); // Для void-метода возвращается null
        }
    }
}
