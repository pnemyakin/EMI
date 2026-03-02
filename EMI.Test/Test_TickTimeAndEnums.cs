namespace EMI.Test
{
    [TestClass]
    public class Test_TickTime
    {
        [TestMethod("TickTime.Now возвращает ненулевое значение")]
        public void Now_ReturnsNonDefault()
        {
            var now = TickTime.Now;
            Assert.AreNotEqual(default(DateTime), now);
        }

        [TestMethod("TickTime.Now монотонно растёт")]
        public void Now_IsMonotonicallyIncreasing()
        {
            var t1 = TickTime.Now;
            // Спин немного чтобы гарантировать разницу
            for (int i = 0; i < 1000; i++) { }
            var t2 = TickTime.Now;
            Assert.IsTrue(t2 >= t1, $"t2 ({t2.Ticks}) должно быть >= t1 ({t1.Ticks})");
        }

        [TestMethod("Два вызова TickTime.Now дают близкие значения")]
        public void Now_ConsecutiveCalls_CloseValues()
        {
            var t1 = TickTime.Now;
            var t2 = TickTime.Now;
            var diff = t2 - t1;

            // Разница между двумя последовательными вызовами должна быть < 1 секунды
            Assert.IsTrue(diff.TotalSeconds < 1.0, $"Разница {diff.TotalSeconds}s слишком велика");
        }
    }

    [TestClass]
    public class Test_PacketType
    {
        [TestMethod("Все значения PacketType уникальны")]
        public void AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(PacketType)).Cast<byte>().ToList();
            Assert.AreEqual(values.Count, values.Distinct().Count());
        }

        [TestMethod("PacketType содержит ожидаемые значения")]
        public void ExpectedValues_Exist()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), (byte)0)); // None
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.Ping_Send));
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.Ping_Receive));
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.RPC_Simple));
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.RPC_Return));
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.RPC_Returned));
            Assert.IsTrue(Enum.IsDefined(typeof(PacketType), PacketType.RPC_Forwarding));
        }

        [TestMethod("PacketType.None == 0")]
        public void None_IsZero()
        {
            Assert.AreEqual(0, (byte)PacketType.None);
        }
    }

    [TestClass]
    public class Test_RCType
    {
        [TestMethod("Все значения RCType уникальны")]
        public void AllValues_AreUnique()
        {
            var values = Enum.GetValues(typeof(RCType)).Cast<byte>().ToList();
            Assert.AreEqual(values.Count, values.Distinct().Count());
        }

        [TestMethod("RCType содержит ожидаемые значения")]
        public void ExpectedValues_Exist()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(RCType), RCType.Fast));
            Assert.IsTrue(Enum.IsDefined(typeof(RCType), RCType.Guaranteed));
            Assert.IsTrue(Enum.IsDefined(typeof(RCType), RCType.ReturnWait));
            Assert.IsTrue(Enum.IsDefined(typeof(RCType), RCType.Forwarding));
            Assert.IsTrue(Enum.IsDefined(typeof(RCType), RCType.FastForwarding));
        }

        [TestMethod("Количество значений RCType == 5")]
        public void Count_IsFive()
        {
            Assert.AreEqual(5, Enum.GetValues(typeof(RCType)).Length);
        }
    }

    // RequestRatePing.cs (Ping class) не включён в EMI.csproj — dead code, тесты убраны
}
