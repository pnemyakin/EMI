using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.Network
{
    using NGC;

    /// <summary>
    /// Декоратор INetworkClient, применяющий цепочку IPacketMiddleware к пакетам.
    /// Send: middleware[0] → middleware[1] → ... → wire
    /// Accept: wire → middleware[N-1] → ... → middleware[0]
    /// При отсутствии middleware — нулевой overhead (прямой проброс).
    /// </summary>
    public class MiddlewareNetworkClient : INetworkClient
    {
        private readonly INetworkClient _inner;
        private readonly IPacketMiddleware[] _middlewares;

        /// <summary>
        /// Дескриптор совместимости: массив [id, version, id, version, ...] всех middleware по порядку
        /// </summary>
        internal byte[] CompatibilityDescriptor { get; }

        /// <inheritdoc/>
        public bool IsConnect => _inner.IsConnect;

        /// <inheritdoc/>
        public int SendByteSpeed => _inner.SendByteSpeed;

        /// <inheritdoc/>
        public float DeliveredRate => _inner.DeliveredRate;

        /// <inheritdoc/>
        public RandomDropType RandomDrop
        {
            get => _inner.RandomDrop;
            set => _inner.RandomDrop = value;
        }

        /// <inheritdoc/>
        public event INetworkClientDisconnected Disconnected
        {
            add => _inner.Disconnected += value;
            remove => _inner.Disconnected -= value;
        }

        /// <summary>
        /// Создаёт MiddlewareNetworkClient
        /// </summary>
        /// <param name="inner">реальный сетевой клиент</param>
        /// <param name="middlewares">цепочка middleware (порядок: compress, encrypt). null или пустой = без middleware.</param>
        public MiddlewareNetworkClient(INetworkClient inner, params IPacketMiddleware[] middlewares)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _middlewares = middlewares ?? Array.Empty<IPacketMiddleware>();

            // Build compatibility descriptor
            CompatibilityDescriptor = new byte[_middlewares.Length * 2];
            for (int i = 0; i < _middlewares.Length; i++)
            {
                CompatibilityDescriptor[i * 2] = _middlewares[i].Id;
                CompatibilityDescriptor[i * 2 + 1] = _middlewares[i].Version;
            }
        }

        /// <inheritdoc/>
        public Task<bool> Connect(string address, CancellationToken token)
        {
            return _inner.Connect(address, token);
        }

        /// <inheritdoc/>
        public void Disconnect(string user_error)
        {
            _inner.Disconnect(user_error);
        }

        /// <inheritdoc/>
        public async Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            if (_middlewares.Length == 0)
            {
                await _inner.Send(array, guaranteed, token).ConfigureAwait(false);
                return;
            }

            // Apply middleware chain: [0] → [1] → ... → wire
            INGCArray current = array;
            INGCArray previous = null;
            try
            {
                for (int i = 0; i < _middlewares.Length; i++)
                {
                    var next = _middlewares[i].ProcessOutgoing(current);
                    // Dispose intermediate buffers (but NOT the original input — caller owns it)
                    if (previous != null)
                        previous.Dispose();
                    previous = current != array ? current : null;
                    current = next;
                }

                // Dispose last intermediate if exists
                if (previous != null && previous != current)
                    previous.Dispose();

                await _inner.Send(current, guaranteed, token).ConfigureAwait(false);
            }
            finally
            {
                // Dispose final transformed buffer (if it's not the original)
                if (current != null && current != array)
                    current.Dispose();
            }
        }

        /// <inheritdoc/>
        public async Task<INGCArray> AcceptPacket(int max_size, CancellationToken token)
        {
            var raw = await _inner.AcceptPacket(max_size, token).ConfigureAwait(false);

            if (_middlewares.Length == 0 || raw.IsEmpty())
                return raw;

            // Apply middleware chain in reverse: wire → [N-1] → ... → [0]
            INGCArray current = raw;
            try
            {
                for (int i = _middlewares.Length - 1; i >= 0; i--)
                {
                    var next = _middlewares[i].ProcessIncoming(current);
                    // Dispose previous (raw or intermediate)
                    current.Dispose();
                    current = next;
                }
                return current;
            }
            catch
            {
                // On error, dispose whatever we have
                if (current != raw)
                    current?.Dispose();
                raw?.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public string GetRemoteClientAddress()
        {
            return _inner.GetRemoteClientAddress();
        }

        #region Handshake Protocol

        // Handshake v3 Protocol (binary, over raw INetworkClient BEFORE middleware is applied).
        // Ключ больше НЕ передаётся открытым текстом — его согласует IKeyExchange.
        //
        // Server → Client:
        //   [MAGIC_HI:1] [MAGIC_LO:1] [VERSION:1]
        //   [flags:1 (bit0=compress, bit1=encrypt)]
        //   [kexId:1]                              — идентификатор стратегии обмена ключами
        //   [offerLen:2 LE] [offer: offerLen]      — payload стратегии (RSA pubkey и т.п.)
        //   [descLen:1] [descriptor: descLen]
        //
        // Client → Server:
        //   [MAGIC_HI:1] [MAGIC_LO:1] [VERSION:1]
        //   [flags:1] [kexId:1]
        //   [respLen:2 LE] [response: respLen]     — payload ответа стратегии (зашифр. ключ и т.п.)
        //   [descLen:1] [descriptor: descLen]
        //
        // Обе стороны сверяют flags/kexId/descriptor. Несовпадение → строка ошибки.

        private const byte MAGIC_HI = 0xE1;
        private const byte MAGIC_LO = 0x4D;
        private const byte VERSION = 3;
        private const byte FLAG_COMPRESS = 0x01;
        private const byte FLAG_ENCRYPT = 0x02;

        /// <summary>
        /// Результат handshake. Для обеих сторон: Error (null = ок) и готовая цепочка middleware.
        /// </summary>
        public struct HandshakeResult
        {
            /// <summary>Ошибка (null = успех)</summary>
            public string Error;
            /// <summary>Готовые middleware (с согласованным ключом); null = без middleware</summary>
            public IPacketMiddleware[] Middlewares;
        }

        private const int HANDSHAKE_MAX = 4096; // RSA-2048 SPKI ~294 B, с запасом

        /// <summary>
        /// Строит дескриптор совместимости детерминированно из флагов
        /// (одинаково на клиенте и сервере, не требует созданных middleware).
        /// Массив пар [id, version, ...] по порядку цепочки.
        /// </summary>
        private static byte[] BuildDescriptor(bool compress, bool encrypt)
        {
            int n = (compress ? 1 : 0) + (encrypt ? 1 : 0);
            var d = new byte[n * 2];
            int p = 0;
            if (compress) { d[p++] = MiddlewareIds.LZ4; d[p++] = 1; }
            if (encrypt) { d[p++] = MiddlewareIds.AesGcm; d[p++] = 1; }
            return d;
        }

        /// <summary>
        /// Строит цепочку middleware из флага сжатия и согласованного ключа
        /// (шифрование включается наличием ключа).
        /// </summary>
        private static IPacketMiddleware[] BuildChain(bool compress, byte[] key)
        {
            var list = new System.Collections.Generic.List<IPacketMiddleware>(2);
            if (compress) list.Add(new LZ4Middleware());
            if (key != null) list.Add(new AesGcmMiddleware(key));
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>
        /// Handshake на стороне СЕРВЕРА (протокол v3): договаривается о флагах и стратегии обмена
        /// ключами, выполняет обмен (ключ НЕ передаётся открытым текстом), строит middleware.
        /// Вызывается НА СЫРОМ INetworkClient (до middleware).
        /// </summary>
        /// <param name="rawClient">сырой клиент (без middleware)</param>
        /// <param name="useCompression">включить LZ4-сжатие</param>
        /// <param name="keyExchange">стратегия обмена ключами (см. лестницу уровней)</param>
        /// <param name="token">токен отмены</param>
        /// <returns>Error=null и Middlewares для обёртки клиента, либо Error с описанием</returns>
        public static async Task<HandshakeResult> PerformServerHandshake(
            INetworkClient rawClient, bool useCompression, IKeyExchange keyExchange,
            CancellationToken token)
        {
            keyExchange = keyExchange ?? NoKeyExchange.Instance;
            bool useEncryption = keyExchange.ProducesKey;

            byte flags = 0;
            if (useCompression) flags |= FLAG_COMPRESS;
            if (useEncryption) flags |= FLAG_ENCRYPT;
            byte[] descriptor = BuildDescriptor(useCompression, useEncryption);
            byte[] offer = useEncryption ? keyExchange.ServerOffer() : Array.Empty<byte>();

            await SendHandshakePacket(rawClient, flags, keyExchange.Id, offer, descriptor, token)
                .ConfigureAwait(false);

            var recv = await rawClient.AcceptPacket(HANDSHAKE_MAX, token).ConfigureAwait(false);
            try
            {
                var parsed = ParseHandshakePacket(recv, "client");
                if (parsed.Error != null) return new HandshakeResult { Error = parsed.Error };
                if (parsed.Flags != flags)
                    return new HandshakeResult { Error = $"Handshake: флаги клиента ({parsed.Flags}) не совпадают с серверными ({flags})" };
                if (parsed.KeyExchangeId != keyExchange.Id)
                    return new HandshakeResult { Error = $"Handshake: стратегия ключа клиента ({parsed.KeyExchangeId}) != серверной ({keyExchange.Id})" };
                string descErr = CompareDescriptor(parsed, descriptor);
                if (descErr != null) return new HandshakeResult { Error = descErr };

                byte[] key = useEncryption ? keyExchange.ServerComplete(parsed.Payload) : null;
                return new HandshakeResult { Error = null, Middlewares = BuildChain(useCompression, key) };
            }
            catch (Exception ex)
            {
                return new HandshakeResult { Error = "Handshake (server) failed: " + ex.Message };
            }
            finally
            {
                recv.Dispose();
            }
        }

        /// <summary>
        /// Handshake на стороне КЛИЕНТА: получает ключ + дескриптор от сервера, строит middleware, отправляет свой дескриптор.
        /// </summary>
        /// <param name="rawClient">сырой клиент</param>
        /// <param name="useCompression">клиент хочет сжатие?</param>
        /// <param name="keyExchange">стратегия обмена ключами (та же ступень, что и на сервере)</param>
        /// <param name="token">токен отмены</param>
        public static async Task<HandshakeResult> PerformClientHandshake(
            INetworkClient rawClient, bool useCompression, IKeyExchange keyExchange,
            CancellationToken token)
        {
            keyExchange = keyExchange ?? NoKeyExchange.Instance;
            bool useEncryption = keyExchange.ProducesKey;

            var recv = await rawClient.AcceptPacket(HANDSHAKE_MAX, token).ConfigureAwait(false);
            HandshakePacket srv;
            try
            {
                srv = ParseHandshakePacket(recv, "server");
            }
            finally
            {
                recv.Dispose();
            }
            if (srv.Error != null) return new HandshakeResult { Error = srv.Error };

            byte flags = 0;
            if (useCompression) flags |= FLAG_COMPRESS;
            if (useEncryption) flags |= FLAG_ENCRYPT;
            byte[] descriptor = BuildDescriptor(useCompression, useEncryption);

            // Несовпадение конфигурации — всё равно шлём ответ (чтобы сервер не завис), затем ошибка.
            string cfgErr = null;
            if (srv.Flags != flags)
                cfgErr = $"Handshake: флаги сервера ({srv.Flags}) не совпадают с клиентскими ({flags})";
            else if (srv.KeyExchangeId != keyExchange.Id)
                cfgErr = $"Handshake: стратегия ключа сервера ({srv.KeyExchangeId}) != клиентской ({keyExchange.Id})";
            else
                cfgErr = CompareDescriptor(srv, descriptor);

            byte[] response = Array.Empty<byte>();
            byte[] key = null;
            if (cfgErr == null && useEncryption)
            {
                try { key = keyExchange.ClientComplete(srv.Payload, out response); }
                catch (Exception ex) { cfgErr = "Handshake (client) key exchange failed: " + ex.Message; }
            }

            await SendHandshakePacket(rawClient, flags, keyExchange.Id, response, descriptor, token)
                .ConfigureAwait(false);

            if (cfgErr != null) return new HandshakeResult { Error = cfgErr };
            return new HandshakeResult { Error = null, Middlewares = BuildChain(useCompression, key) };
        }

        /// <summary>Разобранный handshake-пакет протокола v3.</summary>
        private struct HandshakePacket
        {
            public string Error;
            public byte Flags;
            public byte KeyExchangeId;
            public byte[] Payload;     // offer (от сервера) или response (от клиента)
            public byte[] Descriptor;
        }

        /// <summary>
        /// Отправляет handshake-пакет v3: magic+version, flags, kexId, payload (len16), descriptor (len8).
        /// </summary>
        private static async Task SendHandshakePacket(
            INetworkClient rawClient, byte flags, byte kexId,
            byte[] payload, byte[] descriptor, CancellationToken token)
        {
            payload = payload ?? Array.Empty<byte>();
            descriptor = descriptor ?? Array.Empty<byte>();
            if (payload.Length > ushort.MaxValue) throw new InvalidOperationException("handshake payload too large");
            if (descriptor.Length > byte.MaxValue) throw new InvalidOperationException("handshake descriptor too large");

            // magic(2)+ver(1)+flags(1)+kex(1)+payloadLen(2)+payload+descLen(1)+desc
            int size = 2 + 1 + 1 + 1 + 2 + payload.Length + 1 + descriptor.Length;
            var arr = new EasyArray(size);
            var b = arr.Bytes;
            int p = 0;
            b[p++] = MAGIC_HI; b[p++] = MAGIC_LO; b[p++] = VERSION;
            b[p++] = flags; b[p++] = kexId;
            b[p++] = (byte)(payload.Length & 0xFF);
            b[p++] = (byte)(payload.Length >> 8);
            if (payload.Length > 0) { Buffer.BlockCopy(payload, 0, b, p, payload.Length); p += payload.Length; }
            b[p++] = (byte)descriptor.Length;
            if (descriptor.Length > 0) Buffer.BlockCopy(descriptor, 0, b, p, descriptor.Length);

            await rawClient.Send(arr, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Разбирает входящий handshake-пакет v3. peer — "server"/"client" для сообщений об ошибке.
        /// </summary>
        private static HandshakePacket ParseHandshakePacket(INGCArray recv, string peer)
        {
            if (recv.IsEmpty())
                return new HandshakePacket { Error = $"Handshake failed: {peer} disconnected" };

            var b = recv.Bytes;
            int o = recv.Offset;
            int len = recv.Length;

            // magic(2)+ver(1)+flags(1)+kex(1)+payloadLen(2)+descLen(1) = 8 минимум
            if (len < 8)
                return new HandshakePacket { Error = $"Handshake failed: {peer} packet too short" };
            if (b[o] != MAGIC_HI || b[o + 1] != MAGIC_LO)
                return new HandshakePacket { Error = $"Handshake failed: invalid magic from {peer}" };
            if (b[o + 2] != VERSION)
                return new HandshakePacket { Error = $"Handshake failed: {peer} protocol version {b[o + 2]} != {VERSION}" };

            byte flags = b[o + 3];
            byte kexId = b[o + 4];
            int payloadLen = b[o + 5] | (b[o + 6] << 8);
            int p = o + 7;
            if (len < 7 + payloadLen + 1)
                return new HandshakePacket { Error = $"Handshake failed: {peer} payload truncated" };
            var payload = new byte[payloadLen];
            if (payloadLen > 0) Buffer.BlockCopy(b, p, payload, 0, payloadLen);
            p += payloadLen;

            byte descLen = b[p++];
            if (len < (p - o) + descLen)
                return new HandshakePacket { Error = $"Handshake failed: {peer} descriptor truncated" };
            var desc = new byte[descLen];
            if (descLen > 0) Buffer.BlockCopy(b, p, desc, 0, descLen);

            return new HandshakePacket { Error = null, Flags = flags, KeyExchangeId = kexId, Payload = payload, Descriptor = desc };
        }

        /// <summary>Сверяет дескриптор из пакета с ожидаемым; null = совпало.</summary>
        private static string CompareDescriptor(HandshakePacket p, byte[] expected)
        {
            var got = p.Descriptor ?? Array.Empty<byte>();
            bool same = got.Length == expected.Length;
            if (same)
                for (int i = 0; i < got.Length; i++)
                    if (got[i] != expected[i]) { same = false; break; }
            if (same) return null;
            return $"Middleware incompatible! Local=[{FormatDescriptor(expected, 0, expected.Length)}], " +
                   $"Remote=[{FormatDescriptor(got, 0, got.Length)}]";
        }

        #endregion

        private static string FormatDescriptor(byte[] data, int offset, int length)
        {
            if (length == 0) return "none";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < length; i += 2)
            {
                if (i > 0) sb.Append(", ");
                byte id = data[offset + i];
                byte ver = (i + 1 < length) ? data[offset + i + 1] : (byte)0;
                string name;
                switch (id)
                {
                    case MiddlewareIds.LZ4: name = "LZ4"; break;
                    case MiddlewareIds.AesGcm: name = "AES-GCM"; break;
                    default: name = $"Unknown({id})"; break;
                }
                sb.Append($"{name} v{ver}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Константы идентификаторов middleware
    /// </summary>
    public static class MiddlewareIds
    {
        /// <summary>LZ4 сжатие</summary>
        public const byte LZ4 = 1;
        /// <summary>AES-GCM шифрование</summary>
        public const byte AesGcm = 2;
    }
}
