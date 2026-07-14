using System;
using System.Security.Cryptography;

namespace EMI.Network
{
    /// <summary>
    /// Уровни 2–3: RSA key-transport. Клиент генерирует случайный AES-ключ и отправляет его
    /// серверу зашифрованным публичным ключом сервера (RSA-OAEP-SHA256). Расшифровать может
    /// только сервер своим приватным ключом.
    /// <list type="bullet">
    /// <item><b>Уровень 2 (inline):</b> клиент доверяет публичному ключу, полученному в offer.
    /// Защищает от <b>пассивной прослушки</b>, но НЕ от активного MITM.</item>
    /// <item><b>Уровень 3 (pinned):</b> клиент заранее знает публичный ключ сервера и сверяет
    /// с полученным. Защищает и от <b>активного MITM</b>.</item>
    /// </list>
    /// Ключ уникален на каждое соединение (клиент генерирует свежий). Forward secrecy НЕТ:
    /// при утечке приватного ключа сервера ранее записанный трафик можно расшифровать.
    /// Для forward secrecy используйте ECDHE-стратегию.
    /// </summary>
    public sealed class RsaKeyExchange : IKeyExchange, IDisposable
    {
        private const int KEY_BITS = 2048;
        private const int AES_KEY_SIZE = 32; // AES-256

        // Сервер: приватный ключ (расшифровывает). Клиент: null.
        private readonly RSA _serverRsa;
        private readonly object _serverLock = new object();

        // Клиент с пиннингом: ожидаемый публичный ключ сервера (SubjectPublicKeyInfo/DER). Иначе null.
        private readonly byte[] _pinnedPublicKey;

        private RsaKeyExchange(RSA serverRsa, byte[] pinnedPublicKey)
        {
            _serverRsa = serverRsa;
            _pinnedPublicKey = pinnedPublicKey;
        }

        /// <inheritdoc/>
        public byte Id => KeyExchangeIds.Rsa;
        /// <inheritdoc/>
        public string Name => "RSA";
        /// <inheritdoc/>
        public bool ProducesKey => true;

        // ─────────────────────────── Фабрики ───────────────────────────

        /// <summary>
        /// СЕРВЕР: создать стратегию со свежей RSA-парой (генерируется при старте, живёт всё
        /// время работы сервера — клиенты могут её пиннить). Публичный ключ для клиентов
        /// получить через <see cref="ExportPublicKey"/>.
        /// </summary>
        public static RsaKeyExchange CreateServer()
        {
            var rsa = RSA.Create();
            rsa.KeySize = KEY_BITS;
            return new RsaKeyExchange(rsa, null);
        }

        /// <summary>
        /// СЕРВЕР: создать стратегию из ранее сохранённого приватного ключа
        /// (PKCS#8, см. <see cref="ExportPrivateKey"/>). Позволяет держать стабильный
        /// публичный ключ между перезапусками, чтобы клиенты могли его пиннить.
        /// </summary>
        public static RsaKeyExchange CreateServer(byte[] pkcs8PrivateKey)
        {
            if (pkcs8PrivateKey == null) throw new ArgumentNullException(nameof(pkcs8PrivateKey));
            var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);
            return new RsaKeyExchange(rsa, null);
        }

        /// <summary>
        /// КЛИЕНТ, уровень 2: доверять публичному ключу из offer сервера
        /// (защита от пассивной прослушки, НЕ от MITM).
        /// </summary>
        public static RsaKeyExchange CreateClient() => new RsaKeyExchange(null, null);

        /// <summary>
        /// КЛИЕНТ, уровень 3: сверять публичный ключ сервера с закреплённым
        /// (защита в т.ч. от активного MITM). <paramref name="pinnedPublicKey"/> —
        /// SubjectPublicKeyInfo/DER, полученный из <see cref="ExportPublicKey"/>.
        /// </summary>
        public static RsaKeyExchange CreateClientPinned(byte[] pinnedPublicKey)
        {
            if (pinnedPublicKey == null) throw new ArgumentNullException(nameof(pinnedPublicKey));
            return new RsaKeyExchange(null, (byte[])pinnedPublicKey.Clone());
        }

        // ─────────────────────── Экспорт ключей ───────────────────────

        /// <summary>СЕРВЕР: публичный ключ (SubjectPublicKeyInfo/DER) — раздать клиентам для пиннинга.</summary>
        public byte[] ExportPublicKey()
        {
            if (_serverRsa == null) throw new InvalidOperationException("ExportPublicKey доступен только на серверной стратегии");
            return _serverRsa.ExportSubjectPublicKeyInfo();
        }

        /// <summary>СЕРВЕР: приватный ключ (PKCS#8) — сохранить, чтобы публичный ключ был стабилен между перезапусками. Хранить в секрете!</summary>
        public byte[] ExportPrivateKey()
        {
            if (_serverRsa == null) throw new InvalidOperationException("ExportPrivateKey доступен только на серверной стратегии");
            return _serverRsa.ExportPkcs8PrivateKey();
        }

        // ─────────────────────── Обмен ключами ───────────────────────

        /// <inheritdoc/>
        public byte[] ServerOffer()
        {
            if (_serverRsa == null) throw new InvalidOperationException("ServerOffer доступен только на серверной стратегии");
            // offer = публичный ключ сервера (SubjectPublicKeyInfo/DER)
            lock (_serverLock)
                return _serverRsa.ExportSubjectPublicKeyInfo();
        }

        /// <inheritdoc/>
        public byte[] ServerComplete(byte[] clientResponse)
        {
            if (_serverRsa == null) throw new InvalidOperationException("ServerComplete доступен только на серверной стратегии");
            if (clientResponse == null || clientResponse.Length == 0)
                throw new InvalidOperationException("RSA: пустой response от клиента");
            // response = AES-ключ, зашифрованный нашим публичным ключом → расшифровываем приватным
            lock (_serverLock)
                return _serverRsa.Decrypt(clientResponse, RSAEncryptionPadding.OaepSHA256);
        }

        /// <inheritdoc/>
        public byte[] ClientComplete(byte[] serverOffer, out byte[] response)
        {
            if (serverOffer == null || serverOffer.Length == 0)
                throw new InvalidOperationException("RSA: пустой offer от сервера");

            // Уровень 3: сверяем публичный ключ сервера с закреплённым до любого использования.
            if (_pinnedPublicKey != null && !FixedTimeEquals(serverOffer, _pinnedPublicKey))
                throw new InvalidOperationException(
                    "RSA pinning: публичный ключ сервера не совпадает с закреплённым (возможен MITM)");

            using (var rsa = RSA.Create())
            {
                rsa.ImportSubjectPublicKeyInfo(serverOffer, out _);

                // Свежий случайный AES-256 ключ на это соединение
                var sessionKey = new byte[AES_KEY_SIZE];
                using (var rng = RandomNumberGenerator.Create())
                    rng.GetBytes(sessionKey);

                response = rsa.Encrypt(sessionKey, RSAEncryptionPadding.OaepSHA256);
                return sessionKey;
            }
        }

        /// <summary>Сравнение за постоянное время (защита от timing-атак при сверке ключа).</summary>
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>Освобождает RSA-ресурсы (серверная стратегия).</summary>
        public void Dispose() => _serverRsa?.Dispose();
    }
}
