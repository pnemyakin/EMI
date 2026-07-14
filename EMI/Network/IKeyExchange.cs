using System.Threading;
using System.Threading.Tasks;

namespace EMI.Network
{
    using NGC;

    /// <summary>
    /// Стратегия согласования сессионного ключа шифрования во время handshake.
    /// <para>
    /// Выполняется на «сыром» <see cref="INetworkClient"/> (до применения middleware),
    /// потому что ключ ещё не согласован. Обмен укладывается в один round-trip:
    /// сервер отправляет offer, клиент отвечает response.
    /// </para>
    /// <para>
    /// Лестница безопасности (см. docs): <see cref="NoKeyExchange"/> (off),
    /// PreSharedKeyExchange (ключ роздан вне сети), RsaKeyExchange (ключ передаётся
    /// под публичным ключом сервера; с пиннингом — защита от активного MITM).
    /// </para>
    /// </summary>
    public interface IKeyExchange
    {
        /// <summary>
        /// Идентификатор стратегии (для проверки совместимости клиент↔сервер во время handshake).
        /// </summary>
        byte Id { get; }

        /// <summary>
        /// Имя стратегии (для диагностики и сообщений об ошибках).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Требует ли стратегия шифрования канала (true → в цепочку добавляется AES-GCM).
        /// Для <see cref="NoKeyExchange"/> — false.
        /// </summary>
        bool ProducesKey { get; }

        /// <summary>
        /// СЕРВЕР, шаг 1: формирует offer (например, публичный ключ RSA).
        /// Может вернуть пустой массив, если стратегии нечего предлагать (PSK).
        /// </summary>
        byte[] ServerOffer();

        /// <summary>
        /// СЕРВЕР, шаг 2: получив response клиента, вычисляет сессионный ключ.
        /// </summary>
        /// <param name="clientResponse">payload из ответа клиента</param>
        /// <returns>сессионный ключ (16/24/32 байта) или null, если ключ не нужен</returns>
        byte[] ServerComplete(byte[] clientResponse);

        /// <summary>
        /// КЛИЕНТ: получив offer сервера, формирует response и вычисляет сессионный ключ.
        /// </summary>
        /// <param name="serverOffer">payload из offer сервера</param>
        /// <param name="response">payload для отправки серверу</param>
        /// <returns>сессионный ключ (16/24/32 байта) или null, если ключ не нужен</returns>
        byte[] ClientComplete(byte[] serverOffer, out byte[] response);
    }

    /// <summary>
    /// Идентификаторы стратегий обмена ключами (для дескриптора совместимости handshake).
    /// </summary>
    public static class KeyExchangeIds
    {
        /// <summary>Без обмена ключами (шифрование выключено).</summary>
        public const byte None = 0;
        /// <summary>Pre-Shared Key: ключ роздан вне сети, по сети не передаётся.</summary>
        public const byte PreSharedKey = 1;
        /// <summary>RSA key-transport: клиент шлёт AES-ключ под публичным ключом сервера.</summary>
        public const byte Rsa = 2;
    }
}
