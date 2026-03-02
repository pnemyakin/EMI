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

        // Handshake v2 Protocol (binary, over raw INetworkClient BEFORE middleware is applied):
        //
        // Server → Client:
        //   [MAGIC_HI: 1] [MAGIC_LO: 1] [flags: 1 byte (bit0=compress, bit1=encrypt)]
        //   [key_len: 1 byte] [encryption_key: key_len bytes]
        //   [desc_len: 1 byte] [compatibility_descriptor: desc_len bytes]
        //
        // Client → Server:
        //   [MAGIC_HI: 1] [MAGIC_LO: 1] [flags: 1 byte]
        //   [desc_len: 1 byte] [compatibility_descriptor: desc_len bytes]
        //
        // Both sides compare descriptors. If mismatch → error string returned.

        private const byte MAGIC_HI = 0xE1;
        private const byte MAGIC_LO = 0x4D;
        private const byte FLAG_COMPRESS = 0x01;
        private const byte FLAG_ENCRYPT = 0x02;

        /// <summary>
        /// Результат handshake на стороне клиента
        /// </summary>
        public struct HandshakeResult
        {
            /// <summary>Ошибка (null = успех)</summary>
            public string Error;
            /// <summary>Готовые middleware для клиента (с ключом от сервера)</summary>
            public IPacketMiddleware[] Middlewares;
        }

        /// <summary>
        /// Handshake на стороне СЕРВЕРА: отправляет ключ + дескриптор, получает дескриптор от клиента, сравнивает.
        /// Вызывается НА СЫРОМ INetworkClient (до middleware), потому что ключ ещё не у клиента.
        /// </summary>
        /// <param name="rawClient">сырой клиент (без middleware)</param>
        /// <param name="mwClient">middleware-обёрнутый клиент (для дескриптора), может быть null</param>
        /// <param name="encryptionKey">ключ шифрования (null если без шифрования)</param>
        /// <param name="token">токен отмены</param>
        /// <returns>null = OK, иначе строка ошибки</returns>
        public static async Task<string> PerformServerHandshake(
            INetworkClient rawClient, MiddlewareNetworkClient mwClient,
            byte[] encryptionKey, CancellationToken token)
        {
            byte[] descriptor = mwClient?.CompatibilityDescriptor ?? Array.Empty<byte>();
            byte flags = 0;
            if (mwClient != null)
            {
                foreach (var mw in mwClient._middlewares)
                {
                    if (mw.Id == MiddlewareIds.LZ4) flags |= FLAG_COMPRESS;
                    if (mw.Id == MiddlewareIds.AesGcm) flags |= FLAG_ENCRYPT;
                }
            }

            byte keyLen = (encryptionKey != null) ? (byte)encryptionKey.Length : (byte)0;
            byte descLen = (byte)descriptor.Length;

            // Send: [magic(2)] [flags(1)] [keyLen(1)] [key(N)] [descLen(1)] [desc(M)]
            int packetSize = 2 + 1 + 1 + keyLen + 1 + descLen;
            var sendArray = new EasyArray(packetSize);
            int pos = 0;
            sendArray.Bytes[pos++] = MAGIC_HI;
            sendArray.Bytes[pos++] = MAGIC_LO;
            sendArray.Bytes[pos++] = flags;
            sendArray.Bytes[pos++] = keyLen;
            if (keyLen > 0)
            {
                Buffer.BlockCopy(encryptionKey, 0, sendArray.Bytes, pos, keyLen);
                pos += keyLen;
            }
            sendArray.Bytes[pos++] = descLen;
            if (descLen > 0)
                Buffer.BlockCopy(descriptor, 0, sendArray.Bytes, pos, descLen);

            await rawClient.Send(sendArray, true, token).ConfigureAwait(false);

            // Receive client descriptor
            var recvArray = await rawClient.AcceptPacket(256, token).ConfigureAwait(false);
            try
            {
                return ValidateClientResponse(recvArray, descriptor);
            }
            finally
            {
                recvArray.Dispose();
            }
        }

        /// <summary>
        /// Handshake на стороне КЛИЕНТА: получает ключ + дескриптор от сервера, строит middleware, отправляет свой дескриптор.
        /// </summary>
        /// <param name="rawClient">сырой клиент</param>
        /// <param name="useCompression">клиент хочет сжатие?</param>
        /// <param name="useEncryption">клиент хочет шифрование?</param>
        /// <param name="manualMiddlewares">middleware заданные вручную (если не null — используются вместо auto)</param>
        /// <param name="token">токен отмены</param>
        public static async Task<HandshakeResult> PerformClientHandshake(
            INetworkClient rawClient, bool useCompression, bool useEncryption,
            IPacketMiddleware[] manualMiddlewares, CancellationToken token)
        {
            // Receive server packet
            var recvArray = await rawClient.AcceptPacket(256, token).ConfigureAwait(false);
            byte[] serverDescriptor;
            byte[] encryptionKey = null;
            byte serverFlags;

            try
            {
                if (recvArray.IsEmpty())
                    return new HandshakeResult { Error = "Handshake failed: server disconnected" };

                var bytes = recvArray.Bytes;
                int offset = recvArray.Offset;

                if (recvArray.Length < 5) // magic(2) + flags(1) + keyLen(1) + descLen(1)
                    return new HandshakeResult { Error = "Handshake failed: server packet too short" };

                if (bytes[offset] != MAGIC_HI || bytes[offset + 1] != MAGIC_LO)
                    return new HandshakeResult { Error = "Handshake failed: invalid magic (server may not support handshake)" };

                serverFlags = bytes[offset + 2];
                byte keyLen = bytes[offset + 3];

                int pos = offset + 4;
                if (keyLen > 0)
                {
                    if (recvArray.Length < pos + keyLen + 1)
                        return new HandshakeResult { Error = "Handshake failed: key truncated" };
                    encryptionKey = new byte[keyLen];
                    Buffer.BlockCopy(bytes, pos, encryptionKey, 0, keyLen);
                    pos += keyLen;
                }

                byte descLen = bytes[pos++];
                serverDescriptor = new byte[descLen];
                if (descLen > 0)
                {
                    if (recvArray.Length < pos + descLen)
                        return new HandshakeResult { Error = "Handshake failed: descriptor truncated" };
                    Buffer.BlockCopy(bytes, pos, serverDescriptor, 0, descLen);
                }
            }
            finally
            {
                recvArray.Dispose();
            }

            // Build client middleware chain
            IPacketMiddleware[] clientMiddlewares;
            if (manualMiddlewares != null)
            {
                clientMiddlewares = manualMiddlewares;
            }
            else
            {
                var mwList = new System.Collections.Generic.List<IPacketMiddleware>();
                bool serverHasCompress = (serverFlags & FLAG_COMPRESS) != 0;
                bool serverHasEncrypt = (serverFlags & FLAG_ENCRYPT) != 0;

                string mismatchError = null;

                // Проверяем совпадение флагов
                if (useCompression != serverHasCompress)
                {
                    mismatchError = $"Middleware incompatible! Client compression={useCompression}, Server compression={serverHasCompress}";
                }
                else if (useEncryption != serverHasEncrypt)
                {
                    mismatchError = $"Middleware incompatible! Client encryption={useEncryption}, Server encryption={serverHasEncrypt}";
                }

                if (useCompression)
                    mwList.Add(new LZ4Middleware());
                if (useEncryption && encryptionKey != null)
                    mwList.Add(new AesGcmMiddleware(encryptionKey));

                clientMiddlewares = mwList.Count > 0 ? mwList.ToArray() : null;

                // При несовпадении флагов — всё равно отправляем ответ (чтобы сервер не завис), потом возвращаем ошибку
                if (mismatchError != null)
                {
                    await SendClientResponse(rawClient, useCompression, useEncryption, Array.Empty<byte>(), token).ConfigureAwait(false);
                    return new HandshakeResult { Error = mismatchError };
                }
            }

            // Build client descriptor
            byte[] clientDescriptor;
            if (clientMiddlewares != null && clientMiddlewares.Length > 0)
            {
                clientDescriptor = new byte[clientMiddlewares.Length * 2];
                for (int i = 0; i < clientMiddlewares.Length; i++)
                {
                    clientDescriptor[i * 2] = clientMiddlewares[i].Id;
                    clientDescriptor[i * 2 + 1] = clientMiddlewares[i].Version;
                }
            }
            else
            {
                clientDescriptor = Array.Empty<byte>();
            }

            // Compare descriptors
            string descError = null;
            if (serverDescriptor.Length != clientDescriptor.Length)
            {
                descError = $"Middleware incompatible! Local=[{FormatDescriptor(clientDescriptor, 0, clientDescriptor.Length)}], Remote=[{FormatDescriptor(serverDescriptor, 0, serverDescriptor.Length)}]";
            }
            else
            {
                for (int i = 0; i < serverDescriptor.Length; i++)
                {
                    if (serverDescriptor[i] != clientDescriptor[i])
                    {
                        descError = $"Middleware incompatible! Local=[{FormatDescriptor(clientDescriptor, 0, clientDescriptor.Length)}], Remote=[{FormatDescriptor(serverDescriptor, 0, serverDescriptor.Length)}]";
                        break;
                    }
                }
            }

            // Всегда отправляем ответ (чтобы сервер не завис на AcceptPacket)
            await SendClientResponse(rawClient, useCompression, useEncryption, clientDescriptor, token).ConfigureAwait(false);

            if (descError != null)
                return new HandshakeResult { Error = descError };

            return new HandshakeResult { Error = null, Middlewares = clientMiddlewares };
        }

        /// <summary>
        /// Отправляет ответ клиента серверу (magic + flags + descriptor)
        /// </summary>
        private static async Task SendClientResponse(
            INetworkClient rawClient, bool useCompression, bool useEncryption,
            byte[] clientDescriptor, CancellationToken token)
        {
            byte cDescLen = (byte)clientDescriptor.Length;
            byte clientFlags = 0;
            if (useCompression) clientFlags |= FLAG_COMPRESS;
            if (useEncryption) clientFlags |= FLAG_ENCRYPT;

            int sendSize = 2 + 1 + 1 + cDescLen; // magic(2) + flags(1) + descLen(1) + desc
            var sendArray = new EasyArray(sendSize);
            sendArray.Bytes[0] = MAGIC_HI;
            sendArray.Bytes[1] = MAGIC_LO;
            sendArray.Bytes[2] = clientFlags;
            sendArray.Bytes[3] = cDescLen;
            if (cDescLen > 0)
                Buffer.BlockCopy(clientDescriptor, 0, sendArray.Bytes, 4, cDescLen);

            await rawClient.Send(sendArray, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Старый handshake для совместимости (когда middleware заданы вручную без ключа)
        /// </summary>
        public async Task<string> PerformHandshake(CancellationToken token)
        {
            return await PerformServerHandshake(_inner, this, null, token).ConfigureAwait(false);
        }

        private static string ValidateClientResponse(INGCArray recvArray, byte[] expectedDescriptor)
        {
            if (recvArray.IsEmpty())
                return "Handshake failed: client disconnected during handshake";

            var bytes = recvArray.Bytes;
            int offset = recvArray.Offset;

            if (recvArray.Length < 4)
                return "Handshake failed: client packet too short";

            if (bytes[offset] != MAGIC_HI || bytes[offset + 1] != MAGIC_LO)
                return "Handshake failed: invalid magic from client";

            // byte clientFlags = bytes[offset + 2]; // можно проверить при необходимости
            byte descLen = bytes[offset + 3];

            if (recvArray.Length < 4 + descLen)
                return "Handshake failed: client descriptor truncated";

            // Compare descriptors
            if (descLen != expectedDescriptor.Length)
            {
                return $"Middleware incompatible! Server=[{FormatDescriptor(expectedDescriptor, 0, expectedDescriptor.Length)}], Client=[{FormatDescriptor(bytes, offset + 4, descLen)}]";
            }
            for (int i = 0; i < descLen; i++)
            {
                if (bytes[offset + 4 + i] != expectedDescriptor[i])
                {
                    return $"Middleware incompatible! Server=[{FormatDescriptor(expectedDescriptor, 0, expectedDescriptor.Length)}], Client=[{FormatDescriptor(bytes, offset + 4, descLen)}]";
                }
            }
            return null; // OK
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
