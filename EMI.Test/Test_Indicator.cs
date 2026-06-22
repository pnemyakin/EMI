using EMI.Indicators;

namespace EMI.Test
{
    [TestClass]
    public class Test_Indicator
    {
        [TestMethod("Indicator.Func конструктор устанавливает ID из хеша имени")]
        public void Func_Constructor_SetsID()
        {
            var indicator = new Indicator.Func("TestMethod");
            long expectedID = "TestMethod".DeterministicGetHashCode();
            Assert.AreEqual(expectedID, indicator.ID);
        }

        [TestMethod("Indicator.Func Size == 0 (нет параметров)")]
        public void Func_Size_IsZero()
        {
            var indicator = new Indicator.Func("NoParams");
            Assert.AreEqual(0, indicator.Size);
        }

        [TestMethod("Indicator.Func PackUp/UnPack не делают ничего (нет данных)")]
        public void Func_PackUpUnPack_NoOp()
        {
            var indicator = new Indicator.Func("NoOp");
            var arr = new EasyArray(10);

            // Не должно падать
            indicator.PackUp(arr);
            indicator.UnPack(arr);
        }

        [TestMethod("Одинаковые имена дают одинаковый ID")]
        public void SameName_SameID()
        {
            var a = new Indicator.Func("MyMethod");
            var b = new Indicator.Func("MyMethod");
            Assert.AreEqual(a.ID, b.ID);
        }

        [TestMethod("Разные имена дают разные ID")]
        public void DifferentNames_DifferentIDs()
        {
            var a = new Indicator.Func("Method1");
            var b = new Indicator.Func("Method2");
            Assert.AreNotEqual(a.ID, b.ID);
        }

#if DEBUG
        [TestMethod("В DEBUG режиме Name устанавливается")]
        public void Debug_Name_IsSet()
        {
            var indicator = new Indicator.Func("DebugTest");
            Assert.AreEqual("DebugTest", indicator.Name);
        }
#endif

        [TestMethod("Indicator.Func<T1> конструктор работает")]
        public void FuncT1_Constructor()
        {
            var indicator = new Indicator.Func<int>("TypedMethod");
            long expectedID = "TypedMethod".DeterministicGetHashCode();
            Assert.AreEqual(expectedID, indicator.ID);
        }

        [TestMethod("Indicator.FuncOut<TOut> конструктор работает")]
        public void FuncOut_Constructor()
        {
            var indicator = new Indicator.FuncOut<int>("ReturnMethod");
            long expectedID = "ReturnMethod".DeterministicGetHashCode();
            Assert.AreEqual(expectedID, indicator.ID);
        }

        [TestMethod("Множество индикаторов не конфликтуют")]
        public void ManyIndicators_NoConflicts()
        {
            var ids = new HashSet<long>();
            var names = new[]
            {
                "Player.Move", "Player.Jump", "Player.Shoot",
                "Enemy.Spawn", "Enemy.Die", "Enemy.Attack",
                "World.Sync", "World.Load", "World.Save",
                "Chat.Send", "Chat.Receive"
            };

            foreach (var name in names)
            {
                var indicator = new Indicator.Func(name);
                Assert.IsTrue(ids.Add(indicator.ID), $"Коллизия ID для '{name}'");
            }
        }
    }
}
