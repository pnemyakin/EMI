namespace EMI.Network
{
    /// <summary>
    /// Уровень 0: обмена ключами нет, канал не шифруется.
    /// Подходит для доверенной сети (LAN), прототипов или когда транспорт
    /// уже шифрует трафик сам (например, Steam P2P).
    /// </summary>
    public sealed class NoKeyExchange : IKeyExchange
    {
        /// <summary>Единственный разделяемый экземпляр (состояния нет).</summary>
        public static readonly NoKeyExchange Instance = new NoKeyExchange();

        /// <inheritdoc/>
        public byte Id => KeyExchangeIds.None;
        /// <inheritdoc/>
        public string Name => "None";
        /// <inheritdoc/>
        public bool ProducesKey => false;

        /// <inheritdoc/>
        public byte[] ServerOffer() => System.Array.Empty<byte>();
        /// <inheritdoc/>
        public byte[] ServerComplete(byte[] clientResponse) => null;
        /// <inheritdoc/>
        public byte[] ClientComplete(byte[] serverOffer, out byte[] response)
        {
            response = System.Array.Empty<byte>();
            return null;
        }
    }
}
