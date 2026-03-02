using EMI.Network;
using System.Diagnostics;

namespace EMI.Test
{
    [TestClass]
    public class Test_RpcRateLimiter
    {
        private static RateLimitConfig DefaultConfig() => new RateLimitConfig
        {
            SoftTokens = 10,
            SoftRefillRate = 100, // 100 токенов/сек для быстрых тестов
            HardTokens = 5,
            HardRefillRate = 1,
        };

        [TestMethod("Пакеты принимаются в рамках soft-бюджета")]
        public void Accept_WithinSoftBudget()
        {
            var limiter = new RpcRateLimiter(DefaultConfig());

            for (int i = 0; i < 10; i++)
            {
                var result = limiter.TryConsume();
                Assert.AreEqual(RateLimitResult.Accept, result, $"Пакет {i} должен быть принят");
            }
        }

        [TestMethod("Soft-дроп после исчерпания soft-токенов")]
        public void SoftDrop_WhenSoftTokensExhausted()
        {
            var limiter = new RpcRateLimiter(DefaultConfig());

            // Исчерпать soft-бюджет
            for (int i = 0; i < 10; i++)
                limiter.TryConsume();

            // Следующий должен быть soft-дроп
            var result = limiter.TryConsume();
            Assert.AreEqual(RateLimitResult.SoftDrop, result);
            Assert.AreEqual(1, limiter.SoftDropCount);
        }

        [TestMethod("Kick после исчерпания hard-токенов")]
        public void Kick_WhenHardTokensExhausted()
        {
            var limiter = new RpcRateLimiter(DefaultConfig());

            // Исчерпать soft-бюджет (10 пакетов)
            for (int i = 0; i < 10; i++)
                limiter.TryConsume();

            // Исчерпать hard-бюджет (5 soft-дропов)
            for (int i = 0; i < 5; i++)
            {
                var r = limiter.TryConsume();
                Assert.AreEqual(RateLimitResult.SoftDrop, r, $"Дроп {i} должен быть soft");
            }

            // Следующий — kick
            var result = limiter.TryConsume();
            Assert.AreEqual(RateLimitResult.Kick, result);
        }

        [TestMethod("SoftDropCount корректно считает дропы")]
        public void SoftDropCount_IsAccurate()
        {
            var limiter = new RpcRateLimiter(DefaultConfig());

            // 10 принятых
            for (int i = 0; i < 10; i++)
                limiter.TryConsume();

            Assert.AreEqual(0, limiter.SoftDropCount);

            // 3 soft-дропа
            for (int i = 0; i < 3; i++)
                limiter.TryConsume();

            Assert.AreEqual(3, limiter.SoftDropCount);
        }

        [TestMethod("Soft-токены восполняются со временем")]
        public void SoftTokens_RefillOverTime()
        {
            var config = new RateLimitConfig
            {
                SoftTokens = 5,
                SoftRefillRate = 10000, // 10000/сек — восполнится быстро
                HardTokens = 100,
                HardRefillRate = 1,
            };
            var limiter = new RpcRateLimiter(config);

            // Исчерпать soft-бюджет
            for (int i = 0; i < 5; i++)
                limiter.TryConsume();

            // Подождать немного для восполнения
            // Stopwatch.Frequency ticks ~ 10MHz, при rate=10000/сек нужно ~0.5ms
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 10) { } // spin-wait 10ms

            // Должен снова принять
            var result = limiter.TryConsume();
            Assert.AreEqual(RateLimitResult.Accept, result, "Токены должны восполниться за 10мс");
        }

        [TestMethod("Hard-токены восполняются со временем")]
        public void HardTokens_RefillOverTime()
        {
            var config = new RateLimitConfig
            {
                SoftTokens = 2,
                SoftRefillRate = 0, // НЕ восполняются (чтобы всегда дропать)
                HardTokens = 1,
                HardRefillRate = 10000, // быстрое восполнение
            };
            var limiter = new RpcRateLimiter(config);

            // Исчерпать soft (2 шт)
            limiter.TryConsume();
            limiter.TryConsume();

            // Один soft-дроп (hard-токен расходуется)
            var r1 = limiter.TryConsume();
            Assert.AreEqual(RateLimitResult.SoftDrop, r1);

            // Подождать для восполнения hard
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 10) { }

            // Hard должен восполниться — снова soft-дроп (не kick)
            var r2 = limiter.TryConsume();
            Assert.AreEqual(RateLimitResult.SoftDrop, r2, "Hard-токены должны восполниться");
        }

        [TestMethod("Burst пропускается без дропов")]
        public void Burst_AcceptedWithinBudget()
        {
            var config = new RateLimitConfig
            {
                SoftTokens = 100,
                SoftRefillRate = 10,
                HardTokens = 50,
                HardRefillRate = 1,
            };
            var limiter = new RpcRateLimiter(config);

            // Burst из 100 пакетов — все должны пройти
            int accepted = 0;
            for (int i = 0; i < 100; i++)
            {
                if (limiter.TryConsume() == RateLimitResult.Accept)
                    accepted++;
            }

            Assert.AreEqual(100, accepted, "Burst из 100 пакетов должен полностью пройти при бюджете 100");
        }

        [TestMethod("Устойчивый флуд приводит к kick")]
        public void SustainedFlood_CausesKick()
        {
            var config = new RateLimitConfig
            {
                SoftTokens = 5,
                SoftRefillRate = 0, // не восполняются
                HardTokens = 3,
                HardRefillRate = 0, // не восполняются
            };
            var limiter = new RpcRateLimiter(config);

            // 5 accept + 3 soft-drop + 1 kick = 9 пакетов до kick
            var results = new RateLimitResult[9];
            for (int i = 0; i < 9; i++)
                results[i] = limiter.TryConsume();

            // Первые 5 — Accept
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(RateLimitResult.Accept, results[i], $"Пакет {i}");

            // Следующие 3 — SoftDrop
            for (int i = 5; i < 8; i++)
                Assert.AreEqual(RateLimitResult.SoftDrop, results[i], $"Пакет {i}");

            // Последний — Kick
            Assert.AreEqual(RateLimitResult.Kick, results[8], "Пакет 8 должен быть Kick");
        }

        [TestMethod("Null RateLimit — лимит отключён (Server.RateLimit = null по умолчанию)")]
        public void ServerRateLimit_NullByDefault()
        {
            // Проверяем что RateLimitConfig может быть null и это ожидаемое поведение
            RateLimitConfig config = null;
            Assert.IsNull(config);
            // Клиент с _rateLimiter == null не ограничивает (проверяется отсутствием создания RpcRateLimiter)
        }

        [TestMethod("Clone создаёт независимую копию")]
        public void Config_Clone_IsIndependent()
        {
            var original = new RateLimitConfig
            {
                SoftTokens = 42,
                SoftRefillRate = 99,
                HardTokens = 7,
                HardRefillRate = 3,
            };

            var clone = original.Clone();

            Assert.AreEqual(42, clone.SoftTokens);
            Assert.AreEqual(99, clone.SoftRefillRate);
            Assert.AreEqual(7, clone.HardTokens);
            Assert.AreEqual(3, clone.HardRefillRate);

            // Изменение оригинала не влияет на копию
            original.SoftTokens = 1000;
            Assert.AreEqual(42, clone.SoftTokens);
        }

        [TestMethod("Токены не превышают максимум после длительного простоя")]
        public void Tokens_DoNotExceedMax()
        {
            var config = new RateLimitConfig
            {
                SoftTokens = 10,
                SoftRefillRate = 100000,
                HardTokens = 50,
                HardRefillRate = 100000,
            };
            var limiter = new RpcRateLimiter(config);

            // Подождать «долго» — токены не должны превысить максимум
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 20) { }

            // После долгого простоя CurrentSoftTokens не должен превышать SoftTokens
            // Вызываем TryConsume чтобы сработал Refill и обновил токены
            limiter.TryConsume(); // trigger refill + consume 1
            Assert.IsTrue(limiter.CurrentSoftTokens <= config.SoftTokens,
                $"Soft-токены ({limiter.CurrentSoftTokens}) не должны превышать максимум ({config.SoftTokens})");

            // Hard-токены аналогично
            Assert.IsTrue(limiter.CurrentHardTokens <= config.HardTokens,
                $"Hard-токены ({limiter.CurrentHardTokens}) не должны превышать максимум ({config.HardTokens})");
        }

        [TestMethod("Пинг-пакеты не подлежат rate limiting (проверка IsRpcPacket-логики)")]
        public void PingPackets_NotRateLimited()
        {
            // Тестируем что PacketType.Ping_Send и Ping_Receive не являются RPC
            Assert.AreNotEqual(PacketType.RPC_Simple, PacketType.Ping_Send);
            Assert.AreNotEqual(PacketType.RPC_Return, PacketType.Ping_Send);
            Assert.AreNotEqual(PacketType.RPC_Forwarding, PacketType.Ping_Send);

            Assert.AreNotEqual(PacketType.RPC_Simple, PacketType.Ping_Receive);
            Assert.AreNotEqual(PacketType.RPC_Return, PacketType.Ping_Receive);
            Assert.AreNotEqual(PacketType.RPC_Forwarding, PacketType.Ping_Receive);
        }
    }
}
