using System;

namespace EMI.Network
{
    /// <summary>
    /// Уровень 1: Pre-Shared Key. Ключ задан на обеих сторонах заранее (out-of-band:
    /// зашит в билд, конфиг, и т.п.) и <b>по сети не передаётся вообще</b> — перехватывать
    /// нечего, поэтому защищает и от пассивной прослушки, и от активного MITM.
    /// <para>
    /// Единственное требование — доставить ключ обеим сторонам безопасным способом.
    /// Идеально для Headless/Steam P2P, где нет управляемого библиотекой handshake-раунда.
    /// </para>
    /// </summary>
    public sealed class PreSharedKeyExchange : IKeyExchange
    {
        private readonly byte[] _key;

        /// <summary>
        /// Создаёт PSK-стратегию с заданным ключом.
        /// </summary>
        /// <param name="key">ключ 16 (AES-128), 24 (AES-192) или 32 (AES-256) байта</param>
        public PreSharedKeyExchange(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("Key must be 16, 24, or 32 bytes", nameof(key));

            _key = (byte[])key.Clone();
        }

        /// <inheritdoc/>
        public byte Id => KeyExchangeIds.PreSharedKey;
        /// <inheritdoc/>
        public string Name => "PreSharedKey";
        /// <inheritdoc/>
        public bool ProducesKey => true;

        /// <inheritdoc/>
        public byte[] ServerOffer() => Array.Empty<byte>();

        /// <inheritdoc/>
        public byte[] ServerComplete(byte[] clientResponse) => (byte[])_key.Clone();

        /// <inheritdoc/>
        public byte[] ClientComplete(byte[] serverOffer, out byte[] response)
        {
            response = Array.Empty<byte>();
            return (byte[])_key.Clone();
        }
    }
}
