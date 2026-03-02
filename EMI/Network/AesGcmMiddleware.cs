using System;
using System.Security.Cryptography;
using System.Threading;
using EMI.NGC;

namespace EMI.Network
{
    /// <summary>
    /// Middleware шифрования AES-256-GCM.
    /// Формат пакета: [nonce: 12 bytes] [tag: 16 bytes] [ciphertext]
    /// Использует инкрементальный nonce (counter) — не требует ГПСЧ на каждый пакет.
    /// Буферы выделяются из NGCArray пула — минимальный GC impact.
    /// </summary>
    public class AesGcmMiddleware : IPacketMiddleware, IDisposable
    {
        /// <inheritdoc/>
        public byte Id => MiddlewareIds.AesGcm;

        /// <inheritdoc/>
        public string Name => "AES-GCM";

        /// <inheritdoc/>
        public byte Version => 1;

        private const int NONCE_SIZE = 12;
        private const int TAG_SIZE = 16;
        private const int OVERHEAD = NONCE_SIZE + TAG_SIZE;

        private readonly byte[] _key;
        private readonly AesGcm _aes;
        private long _sendCounter;

        // For unique nonce: 4 bytes instanceId + 8 bytes counter
        private readonly byte[] _instanceId;

        /// <summary>
        /// Создаёт AES-GCM middleware с указанным ключом (PSK — Pre-Shared Key)
        /// </summary>
        /// <param name="key">Ключ шифрования: 16 (AES-128), 24 (AES-192) или 32 (AES-256) байт</param>
        public AesGcmMiddleware(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
                throw new ArgumentException("Key must be 16, 24, or 32 bytes", nameof(key));

            _key = new byte[key.Length];
            Buffer.BlockCopy(key, 0, _key, 0, key.Length);

#pragma warning disable CA5401 // Nonce managed manually via counter
            _aes = new AesGcm(_key);
#pragma warning restore CA5401

            // Generate unique instance ID from random for nonce uniqueness across sessions
            _instanceId = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_instanceId);
            }
        }

        /// <inheritdoc/>
        public INGCArray ProcessOutgoing(INGCArray input)
        {
            int plaintextLen = input.Length;
            int totalSize = OVERHEAD + plaintextLen;

            var output = new NGCArray(totalSize);

            // Build nonce: [4 bytes instanceId] [8 bytes counter]
            var nonce = new byte[NONCE_SIZE];
            Buffer.BlockCopy(_instanceId, 0, nonce, 0, 4);
            long counter = Interlocked.Increment(ref _sendCounter);
            WriteInt64LE(nonce, 4, counter);

            // Write nonce to output
            Buffer.BlockCopy(nonce, 0, output.Bytes, 0, NONCE_SIZE);

            // Encrypt: plaintext → ciphertext + tag
            var tag = new byte[TAG_SIZE];

            _aes.Encrypt(
                nonce: nonce,
                plaintext: new ReadOnlySpan<byte>(input.Bytes, input.Offset, plaintextLen),
                ciphertext: new Span<byte>(output.Bytes, OVERHEAD, plaintextLen),
                tag: tag);

            // Write tag after nonce
            Buffer.BlockCopy(tag, 0, output.Bytes, NONCE_SIZE, TAG_SIZE);

            return output;
        }

        /// <inheritdoc/>
        public INGCArray ProcessIncoming(INGCArray input)
        {
            if (input.Length < OVERHEAD)
                throw new InvalidOperationException(
                    $"AesGcmMiddleware: packet too short ({input.Length} bytes, minimum {OVERHEAD})");

            int offset = input.Offset;
            int ciphertextLen = input.Length - OVERHEAD;

            // Extract nonce
            var nonce = new byte[NONCE_SIZE];
            Buffer.BlockCopy(input.Bytes, offset, nonce, 0, NONCE_SIZE);

            // Extract tag
            var tag = new byte[TAG_SIZE];
            Buffer.BlockCopy(input.Bytes, offset + NONCE_SIZE, tag, 0, TAG_SIZE);

            // Decrypt
            var output = new NGCArray(ciphertextLen);
            try
            {
                _aes.Decrypt(
                    nonce: nonce,
                    ciphertext: new ReadOnlySpan<byte>(input.Bytes, offset + OVERHEAD, ciphertextLen),
                    tag: tag,
                    plaintext: new Span<byte>(output.Bytes, 0, ciphertextLen));
            }
            catch (CryptographicException ex)
            {
                output.Dispose();
                throw new InvalidOperationException("AesGcmMiddleware: decryption failed (wrong key or corrupted data)", ex);
            }

            return output;
        }

        /// <summary>
        /// Освобождает ресурсы
        /// </summary>
        public void Dispose()
        {
            _aes?.Dispose();
        }

        private static void WriteInt64LE(byte[] buf, int offset, long value)
        {
            buf[offset] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            buf[offset + 4] = (byte)(value >> 32);
            buf[offset + 5] = (byte)(value >> 40);
            buf[offset + 6] = (byte)(value >> 48);
            buf[offset + 7] = (byte)(value >> 56);
        }
    }
}
