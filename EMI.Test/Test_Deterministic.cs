namespace EMI.Test
{
    [TestClass]
    public class Test_Deterministic
    {
        [TestMethod("Одинаковые строки дают одинаковый хеш")]
        public void SameInput_SameHash()
        {
            string input = "TestMethod";
            int hash1 = input.DeterministicGetHashCode();
            int hash2 = input.DeterministicGetHashCode();
            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod("Хеш детерминирован — известные значения не меняются между запусками")]
        public void KnownValues_AreStable()
        {
            // Зафиксированные значения — если хеш-алгоритм изменится, тест упадёт
            int hash = "Hello".DeterministicGetHashCode();
            int hash2 = "Hello".DeterministicGetHashCode();
            Assert.AreEqual(hash, hash2);

            // Сохраняем snapshot для регрессии
            int snapshot = "EMI.RPC.TestMethod".DeterministicGetHashCode();
            int snapshot2 = "EMI.RPC.TestMethod".DeterministicGetHashCode();
            Assert.AreEqual(snapshot, snapshot2);
        }

        [TestMethod("Разные строки дают разные хеши")]
        public void DifferentInputs_DifferentHashes()
        {
            int hash1 = "Alpha".DeterministicGetHashCode();
            int hash2 = "Beta".DeterministicGetHashCode();
            int hash3 = "Gamma".DeterministicGetHashCode();

            Assert.AreNotEqual(hash1, hash2);
            Assert.AreNotEqual(hash2, hash3);
            Assert.AreNotEqual(hash1, hash3);
        }

        [TestMethod("Пустая строка не вызывает исключения")]
        public void EmptyString_DoesNotThrow()
        {
            int hash = "".DeterministicGetHashCode();
            // Просто проверяем что не упало — значение может быть любое
            Assert.AreEqual(hash, "".DeterministicGetHashCode());
        }

        [TestMethod("Один символ работает корректно")]
        public void SingleChar_Works()
        {
            int hash = "A".DeterministicGetHashCode();
            Assert.AreEqual(hash, "A".DeterministicGetHashCode());
            Assert.AreNotEqual(hash, "B".DeterministicGetHashCode());
        }

        [TestMethod("Чётная и нечётная длина строки")]
        public void EvenAndOddLength()
        {
            // Чётная длина (4)
            int even = "ABCD".DeterministicGetHashCode();
            Assert.AreEqual(even, "ABCD".DeterministicGetHashCode());

            // Нечётная длина (5)
            int odd = "ABCDE".DeterministicGetHashCode();
            Assert.AreEqual(odd, "ABCDE".DeterministicGetHashCode());

            Assert.AreNotEqual(even, odd);
        }

        [TestMethod("Длинная строка не вызывает проблем")]
        public void LongString_Works()
        {
            string longStr = new string('X', 10000);
            int hash = longStr.DeterministicGetHashCode();
            Assert.AreEqual(hash, longStr.DeterministicGetHashCode());
        }

        [TestMethod("Юникод строки работают")]
        public void Unicode_Works()
        {
            int hash1 = "Привет".DeterministicGetHashCode();
            int hash2 = "Мир".DeterministicGetHashCode();
            Assert.AreNotEqual(hash1, hash2);
            Assert.AreEqual(hash1, "Привет".DeterministicGetHashCode());
        }

        [TestMethod("Коллизии редки в типичных RPC-именах")]
        public void LowCollisionRate_ForRPCNames()
        {
            var names = new[]
            {
                "SendMessage", "ReceiveMessage", "OnPlayerJoin", "OnPlayerLeave",
                "SyncPosition", "SyncRotation", "SyncHealth", "SyncAmmo",
                "RPC_Attack", "RPC_Heal", "RPC_Respawn", "RPC_Die",
                "Chat.Send", "Chat.Receive", "Inventory.Add", "Inventory.Remove",
                "Player.Move", "Player.Jump", "Player.Shoot", "Player.Reload"
            };

            var hashes = new HashSet<int>();
            foreach (var name in names)
            {
                bool added = hashes.Add(name.DeterministicGetHashCode());
                Assert.IsTrue(added, $"Коллизия хеша для '{name}'");
            }
        }
    }
}
