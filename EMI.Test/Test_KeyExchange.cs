using System;
using System.Security.Cryptography;
using EMI.Network;

namespace EMI.Test
{
    /// <summary>
    /// Тесты лестницы обмена ключами: PSK, RSA (inline/pinned), детект MITM.
    /// Проверяют саму крипто-логику стратегий (детерминированно, без сети).
    /// </summary>
    [TestClass]
    public class Test_KeyExchange
    {
        private static byte[] Key(int n)
        {
            var k = new byte[n];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(k);
            return k;
        }

        [TestMethod("PSK: обе стороны получают один и тот же ключ, по сети он не идёт")]
        public void Psk_BothSidesSameKey_NoWireTransfer()
        {
            byte[] shared = Key(32);
            var server = new PreSharedKeyExchange(shared);
            var client = new PreSharedKeyExchange(shared);

            byte[] offer = server.ServerOffer();
            Assert.AreEqual(0, offer.Length, "PSK не должен ничего слать в offer");

            byte[] clientKey = client.ClientComplete(offer, out byte[] response);
            Assert.AreEqual(0, response.Length, "PSK не должен слать ключ по сети");

            byte[] serverKey = server.ServerComplete(response);
            CollectionAssert.AreEqual(clientKey, serverKey);
            CollectionAssert.AreEqual(shared, serverKey);
        }

        [TestMethod("RSA inline: клиент шлёт ключ под pubkey сервера, ключ не в открытом виде")]
        public void Rsa_Inline_KeyNotInPlaintext()
        {
            using var server = RsaKeyExchange.CreateServer();
            var client = RsaKeyExchange.CreateClient();

            byte[] offer = server.ServerOffer();            // pubkey сервера
            byte[] clientKey = client.ClientComplete(offer, out byte[] response);

            // response — это зашифрованный ключ; открытого ключа в нём быть не должно
            Assert.IsTrue(response.Length > 0);
            CollectionAssert.AreNotEqual(clientKey, response, "ключ не должен передаваться открыто");
            Assert.IsFalse(Contains(response, clientKey), "сырой ключ найден в сетевом payload!");

            byte[] serverKey = server.ServerComplete(response);
            CollectionAssert.AreEqual(clientKey, serverKey, "сервер должен получить тот же ключ");
            Assert.AreEqual(32, serverKey.Length);
        }

        [TestMethod("RSA: ключ уникален на каждое соединение")]
        public void Rsa_KeyUniquePerConnection()
        {
            using var server = RsaKeyExchange.CreateServer();
            byte[] offer = server.ServerOffer();

            var k1 = RsaKeyExchange.CreateClient().ClientComplete(offer, out _);
            var k2 = RsaKeyExchange.CreateClient().ClientComplete(offer, out _);
            CollectionAssert.AreNotEqual(k1, k2, "ключи разных соединений должны отличаться");
        }

        [TestMethod("RSA pinned: правильный ключ сервера — успех")]
        public void Rsa_Pinned_CorrectKey_Success()
        {
            using var server = RsaKeyExchange.CreateServer();
            byte[] pin = server.ExportPublicKey();
            var client = RsaKeyExchange.CreateClientPinned(pin);

            byte[] offer = server.ServerOffer();
            byte[] clientKey = client.ClientComplete(offer, out byte[] response);
            byte[] serverKey = server.ServerComplete(response);
            CollectionAssert.AreEqual(clientKey, serverKey);
        }

        [TestMethod("RSA pinned: подменённый ключ сервера (MITM) — исключение")]
        public void Rsa_Pinned_MitmKey_Throws()
        {
            using var realServer = RsaKeyExchange.CreateServer();
            using var attacker = RsaKeyExchange.CreateServer();

            // Клиент пиннит НАСТОЯЩИЙ сервер, но получает offer атакующего
            byte[] pin = realServer.ExportPublicKey();
            var client = RsaKeyExchange.CreateClientPinned(pin);
            byte[] mitmOffer = attacker.ServerOffer();

            Assert.ThrowsException<InvalidOperationException>(
                () => client.ClientComplete(mitmOffer, out _),
                "пиннинг должен отвергнуть чужой публичный ключ");
        }

        [TestMethod("RSA server: экспорт/импорт приватного ключа сохраняет пару")]
        public void Rsa_PersistedKey_StablePublicKey()
        {
            byte[] pkcs8, pub;
            using (var s = RsaKeyExchange.CreateServer())
            {
                pkcs8 = s.ExportPrivateKey();
                pub = s.ExportPublicKey();
            }
            using var restored = RsaKeyExchange.CreateServer(pkcs8);
            CollectionAssert.AreEqual(pub, restored.ExportPublicKey(),
                "восстановленный сервер должен иметь тот же публичный ключ (для пиннинга между перезапусками)");
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool m = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { m = false; break; }
                if (m) return true;
            }
            return false;
        }
    }
}
