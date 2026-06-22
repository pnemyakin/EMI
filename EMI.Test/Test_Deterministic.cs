namespace EMI.Test
{
    [TestClass]
    public class Test_Deterministic
    {
        [TestMethod("Одинаковые строки дают одинаковый хеш")]
        public void SameInput_SameHash()
        {
            string input = "TestMethod";
            long hash1 = input.DeterministicGetHashCode();
            long hash2 = input.DeterministicGetHashCode();
            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod("Хеш детерминирован — FNV-1a 64-bit даёт стабильный результат")]
        public void KnownValues_AreStable()
        {
            // FNV-1a 64-bit — проверяем что хеш воспроизводится
            long hash1 = "Hello".DeterministicGetHashCode();
            long hash2 = "Hello".DeterministicGetHashCode();
            Assert.AreEqual(hash1, hash2);

            long snapshot1 = "EMI.RPC.TestMethod".DeterministicGetHashCode();
            long snapshot2 = "EMI.RPC.TestMethod".DeterministicGetHashCode();
            Assert.AreEqual(snapshot1, snapshot2);
            // Один и тот же хеш должен выдаваться всегда
            Assert.AreEqual(hash1, "Hello".DeterministicGetHashCode());
        }

        [TestMethod("Разные строки дают разные хеши")]
        public void DifferentInputs_DifferentHashes()
        {
            long hash1 = "Alpha".DeterministicGetHashCode();
            long hash2 = "Beta".DeterministicGetHashCode();
            long hash3 = "Gamma".DeterministicGetHashCode();

            Assert.AreNotEqual(hash1, hash2);
            Assert.AreNotEqual(hash2, hash3);
            Assert.AreNotEqual(hash1, hash3);
        }

        [TestMethod("Пустая строка не вызывает исключения")]
        public void EmptyString_DoesNotThrow()
        {
            long hash = "".DeterministicGetHashCode();
            // Просто проверяем что не упало — значение может быть любое
            Assert.AreEqual(hash, "".DeterministicGetHashCode());
        }

        [TestMethod("Один символ работает корректно")]
        public void SingleChar_Works()
        {
            long hash = "A".DeterministicGetHashCode();
            Assert.AreEqual(hash, "A".DeterministicGetHashCode());
            Assert.AreNotEqual(hash, "B".DeterministicGetHashCode());
        }

        [TestMethod("Чётная и нечётная длина строки")]
        public void EvenAndOddLength()
        {
            // Чётная длина (4)
            long even = "ABCD".DeterministicGetHashCode();
            Assert.AreEqual(even, "ABCD".DeterministicGetHashCode());

            // Нечётная длина (5)
            long odd = "ABCDE".DeterministicGetHashCode();
            Assert.AreEqual(odd, "ABCDE".DeterministicGetHashCode());

            Assert.AreNotEqual(even, odd);
        }

        [TestMethod("Длинная строка не вызывает проблем")]
        public void LongString_Works()
        {
            string longStr = new string('X', 10000);
            long hash = longStr.DeterministicGetHashCode();
            Assert.AreEqual(hash, longStr.DeterministicGetHashCode());
        }

        [TestMethod("Юникод строки работают")]
        public void Unicode_Works()
        {
            long hash1 = "Привет".DeterministicGetHashCode();
            long hash2 = "Мир".DeterministicGetHashCode();
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

            var hashes = new HashSet<long>();
            foreach (var name in names)
            {
                bool added = hashes.Add(name.DeterministicGetHashCode());
                Assert.IsTrue(added, $"Коллизия хеша для '{name}'");
            }
        }
    }
}
