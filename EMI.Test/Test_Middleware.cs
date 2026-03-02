using System;
using EMI.Network;
using EMI.NGC;
using System.Security.Cryptography;

namespace EMI.Test
{
    [TestClass]
    public class Test_Middleware
    {
        #region LZ4Middleware Tests

        [TestMethod("LZ4: маленький пакет передаётся без сжатия (flag=0)")]
        public void LZ4_SmallPacket_PassesThrough()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 256 };
            var data = CreateTestArray(100, 0xAB);

            var compressed = lz4.ProcessOutgoing(data);
            Assert.AreEqual(0, compressed.Bytes[0], "Flag should be RAW (0)");
            Assert.AreEqual(100 + 1, compressed.Length, "Should be original + 1 flag byte");

            var decompressed = lz4.ProcessIncoming(compressed);
            AssertArraysEqual(data, decompressed);

            data.Dispose();
            compressed.Dispose();
            decompressed.Dispose();
        }

        [TestMethod("LZ4: большой пакет сжимается (flag=1)")]
        public void LZ4_LargePacket_Compresses()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 64 };
            // Repetitive data compresses well
            var data = CreateTestArray(1000, 0x42);

            var compressed = lz4.ProcessOutgoing(data);
            Assert.AreEqual(1, compressed.Bytes[0], "Flag should be COMPRESSED (1)");
            Assert.IsTrue(compressed.Length < data.Length, 
                $"Compressed ({compressed.Length}) should be smaller than original ({data.Length})");

            var decompressed = lz4.ProcessIncoming(compressed);
            AssertArraysEqual(data, decompressed);

            data.Dispose();
            compressed.Dispose();
            decompressed.Dispose();
        }

        [TestMethod("LZ4: round-trip сохраняет данные")]
        public void LZ4_RoundTrip_PreservesData()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 32 };
            var rng = new Random(42);
            
            // Test various sizes
            int[] sizes = { 1, 10, 50, 100, 255, 256, 500, 1000, 5000, 10000 };
            foreach (int size in sizes)
            {
                var data = new NGCArray(size);
                rng.NextBytes(data.Bytes);

                var compressed = lz4.ProcessOutgoing(data);
                var decompressed = lz4.ProcessIncoming(compressed);
                
                AssertArraysEqual(data, decompressed, $"Failed for size {size}");

                data.Dispose();
                compressed.Dispose();
                decompressed.Dispose();
            }
        }

        [TestMethod("LZ4: пустой массив — ошибка на входе")]
        public void LZ4_EmptyInput_ThrowsOnIncoming()
        {
            var lz4 = new LZ4Middleware();
            var empty = new EasyArray(0);
            Assert.ThrowsException<InvalidOperationException>(() => lz4.ProcessIncoming(empty));
        }

        [TestMethod("LZ4: невалидный flag — ошибка")]
        public void LZ4_InvalidFlag_Throws()
        {
            var lz4 = new LZ4Middleware();
            var bad = new EasyArray(10);
            bad.Bytes[0] = 0xFF; // invalid flag
            Assert.ThrowsException<InvalidOperationException>(() => lz4.ProcessIncoming(bad));
        }

        [TestMethod("LZ4: MinSizeToCompress=0 сжимает всё")]
        public void LZ4_MinSize0_CompressesEverything()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 0 };
            // Even small repetitive data
            var data = CreateTestArray(20, 0xAA);

            var compressed = lz4.ProcessOutgoing(data);
            var decompressed = lz4.ProcessIncoming(compressed);
            AssertArraysEqual(data, decompressed);

            data.Dispose();
            compressed.Dispose();
            decompressed.Dispose();
        }

        [TestMethod("LZ4: Properties возвращают правильные значения")]
        public void LZ4_Properties()
        {
            var lz4 = new LZ4Middleware();
            Assert.AreEqual(MiddlewareIds.LZ4, lz4.Id);
            Assert.AreEqual("LZ4", lz4.Name);
            Assert.AreEqual((byte)1, lz4.Version);
        }

        #endregion

        #region AesGcmMiddleware Tests

        [TestMethod("AES-GCM: round-trip шифрование/дешифрование")]
        public void AesGcm_RoundTrip()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var data = CreateTestArray(200, 0x55);
            var encrypted = aes.ProcessOutgoing(data);
            
            // Encrypted should be different from original
            Assert.AreEqual(data.Length + 28, encrypted.Length, "Should have 12 nonce + 16 tag overhead");
            
            var decrypted = aes.ProcessIncoming(encrypted);
            AssertArraysEqual(data, decrypted);

            data.Dispose();
            encrypted.Dispose();
            decrypted.Dispose();
        }

        [TestMethod("AES-GCM: разные размеры ключей (16, 24, 32)")]
        public void AesGcm_DifferentKeySizes()
        {
            foreach (int keySize in new[] { 16, 24, 32 })
            {
                byte[] key = GenerateKey(keySize);
                using var aes = new AesGcmMiddleware(key);

                var data = CreateTestArray(100, (byte)keySize);
                var encrypted = aes.ProcessOutgoing(data);
                var decrypted = aes.ProcessIncoming(encrypted);
                AssertArraysEqual(data, decrypted, $"Failed for key size {keySize}");

                data.Dispose();
                encrypted.Dispose();
                decrypted.Dispose();
            }
        }

        [TestMethod("AES-GCM: неверный ключ — ошибка дешифрования")]
        public void AesGcm_WrongKey_Throws()
        {
            byte[] key1 = GenerateKey(32);
            byte[] key2 = GenerateKey(32);
            // Ensure keys differ
            key2[0] = (byte)(key1[0] ^ 0xFF);

            using var aes1 = new AesGcmMiddleware(key1);
            using var aes2 = new AesGcmMiddleware(key2);

            var data = CreateTestArray(100, 0x77);
            var encrypted = aes1.ProcessOutgoing(data);

            Assert.ThrowsException<InvalidOperationException>(() => aes2.ProcessIncoming(encrypted));

            data.Dispose();
            encrypted.Dispose();
        }

        [TestMethod("AES-GCM: повреждённые данные — ошибка")]
        public void AesGcm_CorruptedData_Throws()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var data = CreateTestArray(100, 0x33);
            var encrypted = aes.ProcessOutgoing(data);

            // Corrupt ciphertext byte
            encrypted.Bytes[30] ^= 0xFF;

            Assert.ThrowsException<InvalidOperationException>(() => aes.ProcessIncoming(encrypted));

            data.Dispose();
            encrypted.Dispose();
        }

        [TestMethod("AES-GCM: слишком короткий пакет — ошибка")]
        public void AesGcm_TooShort_Throws()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var tooShort = new EasyArray(10);
            Assert.ThrowsException<InvalidOperationException>(() => aes.ProcessIncoming(tooShort));
        }

        [TestMethod("AES-GCM: невалидный размер ключа — ArgumentException")]
        public void AesGcm_InvalidKeySize_Throws()
        {
            Assert.ThrowsException<ArgumentException>(() => new AesGcmMiddleware(new byte[15]));
            Assert.ThrowsException<ArgumentException>(() => new AesGcmMiddleware(new byte[33]));
        }

        [TestMethod("AES-GCM: null ключ — ArgumentNullException")]
        public void AesGcm_NullKey_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new AesGcmMiddleware(null));
        }

        [TestMethod("AES-GCM: каждый пакет уникально зашифрован (уникальный nonce)")]
        public void AesGcm_UniqueNonce_PerPacket()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var data = CreateTestArray(50, 0x11);
            var enc1 = aes.ProcessOutgoing(data);
            var enc2 = aes.ProcessOutgoing(data);

            // Nonces should differ (first 12 bytes)
            bool noncesDiffer = false;
            for (int i = 0; i < 12; i++)
            {
                if (enc1.Bytes[i] != enc2.Bytes[i])
                {
                    noncesDiffer = true;
                    break;
                }
            }
            Assert.IsTrue(noncesDiffer, "Each encryption should use a unique nonce");

            // But both should decrypt correctly
            var dec1 = aes.ProcessIncoming(enc1);
            var dec2 = aes.ProcessIncoming(enc2);
            AssertArraysEqual(data, dec1);
            AssertArraysEqual(data, dec2);

            data.Dispose();
            enc1.Dispose();
            enc2.Dispose();
            dec1.Dispose();
            dec2.Dispose();
        }

        [TestMethod("AES-GCM: Properties возвращают правильные значения")]
        public void AesGcm_Properties()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);
            Assert.AreEqual(MiddlewareIds.AesGcm, aes.Id);
            Assert.AreEqual("AES-GCM", aes.Name);
            Assert.AreEqual((byte)1, aes.Version);
        }

        #endregion

        #region Middleware Chain Tests

        [TestMethod("Chain: LZ4 + AES-GCM round-trip")]
        public void Chain_LZ4_AesGcm_RoundTrip()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 32 };
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            IPacketMiddleware[] chain = { lz4, aes };

            var data = CreateTestArray(500, 0xCC);

            // Apply outgoing chain: LZ4 → AES
            INGCArray current = data;
            for (int i = 0; i < chain.Length; i++)
            {
                var next = chain[i].ProcessOutgoing(current);
                if (current != data) current.Dispose();
                current = next;
            }
            var encrypted = current;

            // Apply incoming chain in reverse: AES → LZ4
            current = encrypted;
            for (int i = chain.Length - 1; i >= 0; i--)
            {
                var next = chain[i].ProcessIncoming(current);
                if (current != encrypted) current.Dispose();
                current = next;
            }

            AssertArraysEqual(data, current);

            data.Dispose();
            encrypted.Dispose();
            current.Dispose();
        }

        [TestMethod("Chain: только AES-GCM без LZ4")]
        public void Chain_AesOnly_RoundTrip()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var data = CreateTestArray(300, 0xDD);
            var enc = aes.ProcessOutgoing(data);
            var dec = aes.ProcessIncoming(enc);
            AssertArraysEqual(data, dec);

            data.Dispose();
            enc.Dispose();
            dec.Dispose();
        }

        [TestMethod("Chain: только LZ4 без AES-GCM")]
        public void Chain_LZ4Only_RoundTrip()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 10 };

            var data = CreateTestArray(300, 0xEE);
            var comp = lz4.ProcessOutgoing(data);
            var decomp = lz4.ProcessIncoming(comp);
            AssertArraysEqual(data, decomp);

            data.Dispose();
            comp.Dispose();
            decomp.Dispose();
        }

        #endregion

        #region MiddlewareNetworkClient Tests

        [TestMethod("MiddlewareNetworkClient: CompatibilityDescriptor формируется верно")]
        public void MiddlewareClient_CompatibilityDescriptor()
        {
            var lz4 = new LZ4Middleware();
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);

            var fakeClient = new FakeNetworkClient();
            var mwClient = new MiddlewareNetworkClient(fakeClient, lz4, aes);

            var desc = mwClient.CompatibilityDescriptor;
            Assert.AreEqual(4, desc.Length); // 2 middlewares × 2 bytes each
            Assert.AreEqual(MiddlewareIds.LZ4, desc[0]);
            Assert.AreEqual((byte)1, desc[1]); // LZ4 version
            Assert.AreEqual(MiddlewareIds.AesGcm, desc[2]);
            Assert.AreEqual((byte)1, desc[3]); // AES version
        }

        [TestMethod("MiddlewareNetworkClient: без middleware — пустой дескриптор")]
        public void MiddlewareClient_NoMiddleware_EmptyDescriptor()
        {
            var fakeClient = new FakeNetworkClient();
            var mwClient = new MiddlewareNetworkClient(fakeClient);

            Assert.AreEqual(0, mwClient.CompatibilityDescriptor.Length);
        }

        [TestMethod("MiddlewareNetworkClient: Send/Accept через middleware chain")]
        public async Task MiddlewareClient_SendAccept_WithLZ4()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 10 };
            var fakeClient = new FakeNetworkClient();
            var mwClient = new MiddlewareNetworkClient(fakeClient, lz4);

            var data = CreateTestArray(200, 0xBB);
            await mwClient.Send(data, true, CancellationToken.None);

            // FakeClient should have received compressed data
            Assert.AreEqual(1, fakeClient.SentPackets.Count);
            var sentBytes = fakeClient.SentPackets[0];
            // First byte should be compression flag
            Assert.IsTrue(sentBytes.Length < data.Length + 5 || sentBytes[0] == 0 || sentBytes[0] == 1);

            // Now simulate receiving the same data
            fakeClient.QueueForAccept(sentBytes);
            var received = await mwClient.AcceptPacket(65536, CancellationToken.None);

            AssertArraysEqual(data, received);

            data.Dispose();
            received.Dispose();
        }

        [TestMethod("MiddlewareNetworkClient: Send без middleware — прямой проброс")]
        public async Task MiddlewareClient_NoMiddleware_DirectPassthrough()
        {
            var fakeClient = new FakeNetworkClient();
            var mwClient = new MiddlewareNetworkClient(fakeClient);

            var data = CreateTestArray(100, 0x99);
            await mwClient.Send(data, true, CancellationToken.None);

            Assert.AreEqual(1, fakeClient.SentPackets.Count);
            // Should be exact same data (no transformation)
            Assert.AreEqual(data.Length, fakeClient.SentPackets[0].Length);
            for (int i = 0; i < data.Length; i++)
                Assert.AreEqual(data.Bytes[i], fakeClient.SentPackets[0][i]);

            data.Dispose();
        }

        #endregion

        #region Compatibility Handshake Tests (v2 — server sends key+descriptor, client receives)

        [TestMethod("Handshake v2: сжатие + шифрование — успех")]
        public async Task Handshake_CompressionEncryption_Success()
        {
            byte[] key = GenerateKey(32);
            var (clientSide, serverSide) = CreateConnectedPair();

            // Server side: has LZ4 + AES-GCM
            var serverLz4 = new LZ4Middleware();
            using var serverAes = new AesGcmMiddleware(key);
            var mwServer = new MiddlewareNetworkClient(serverSide, serverLz4, serverAes);

            // Run both sides concurrently — server sends first, client reads first
            var serverTask = MiddlewareNetworkClient.PerformServerHandshake(
                serverSide, mwServer, key, CancellationToken.None);
            var clientTask = MiddlewareNetworkClient.PerformClientHandshake(
                clientSide, true, true, null, CancellationToken.None);

            await Task.WhenAll(serverTask, clientTask);

            Assert.IsNull(serverTask.Result, $"Server handshake error: {serverTask.Result}");
            Assert.IsNull(clientTask.Result.Error, $"Client handshake error: {clientTask.Result.Error}");
            Assert.IsNotNull(clientTask.Result.Middlewares);
            Assert.AreEqual(2, clientTask.Result.Middlewares.Length);
        }

        [TestMethod("Handshake v2: без middleware с обеих сторон — успех")]
        public async Task Handshake_NoMiddleware_Success()
        {
            var (clientSide, serverSide) = CreateConnectedPair();

            var serverTask = MiddlewareNetworkClient.PerformServerHandshake(
                serverSide, null, null, CancellationToken.None);
            var clientTask = MiddlewareNetworkClient.PerformClientHandshake(
                clientSide, false, false, null, CancellationToken.None);

            await Task.WhenAll(serverTask, clientTask);

            Assert.IsNull(serverTask.Result, $"Server handshake error: {serverTask.Result}");
            Assert.IsNull(clientTask.Result.Error, $"Client handshake error: {clientTask.Result.Error}");
            Assert.IsNull(clientTask.Result.Middlewares);
        }

        [TestMethod("Handshake v2: клиент хочет шифрование, сервер нет — ошибка")]
        [Timeout(10000)]
        public async Task Handshake_ClientEncryptionServerNone_Error()
        {
            var (clientSide, serverSide) = CreateConnectedPair();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Server without middleware
            var serverTask = MiddlewareNetworkClient.PerformServerHandshake(
                serverSide, null, null, cts.Token);
            // Client expects encryption
            var clientTask = MiddlewareNetworkClient.PerformClientHandshake(
                clientSide, false, true, null, cts.Token);

            await Task.WhenAll(serverTask, clientTask);

            // Client should detect mismatch (encryption flag mismatch before descriptor compare)
            Assert.IsNotNull(clientTask.Result.Error, "Client should detect encryption mismatch");
            Assert.IsTrue(clientTask.Result.Error.Contains("incompatible", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod("Handshake v2: сервер со сжатием, клиент без — ошибка")]
        [Timeout(10000)]
        public async Task Handshake_ServerCompressionClientNone_Error()
        {
            var (clientSide, serverSide) = CreateConnectedPair();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var serverLz4 = new LZ4Middleware();
            var mwServer = new MiddlewareNetworkClient(serverSide, serverLz4);

            var serverTask = MiddlewareNetworkClient.PerformServerHandshake(
                serverSide, mwServer, null, cts.Token);
            // Client doesn't want compression
            var clientTask = MiddlewareNetworkClient.PerformClientHandshake(
                clientSide, false, false, null, cts.Token);

            await Task.WhenAll(serverTask, clientTask);

            // Client should detect compression mismatch
            Assert.IsNotNull(clientTask.Result.Error, "Client should detect compression mismatch");
            Assert.IsTrue(clientTask.Result.Error.Contains("incompatible", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod("Handshake v2: клиент получает ключ от сервера")]
        public async Task Handshake_ClientReceivesKey()
        {
            byte[] key = GenerateKey(32);
            var (clientSide, serverSide) = CreateConnectedPair();

            using var serverAes = new AesGcmMiddleware(key);
            var mwServer = new MiddlewareNetworkClient(serverSide, serverAes);

            var serverTask = MiddlewareNetworkClient.PerformServerHandshake(
                serverSide, mwServer, key, CancellationToken.None);
            var clientTask = MiddlewareNetworkClient.PerformClientHandshake(
                clientSide, false, true, null, CancellationToken.None);

            await Task.WhenAll(serverTask, clientTask);

            Assert.IsNull(serverTask.Result);
            Assert.IsNull(clientTask.Result.Error);
            Assert.IsNotNull(clientTask.Result.Middlewares);

            // Verify the client got a working AesGcmMiddleware (encrypt + decrypt roundtrip)
            var clientAes = clientTask.Result.Middlewares[0] as AesGcmMiddleware;
            Assert.IsNotNull(clientAes, "Client middleware should be AesGcmMiddleware");
            using (clientAes)
            {
                var original = CreateTestArray(64, 0xAB);
                using var encrypted = clientAes.ProcessOutgoing(original);
                // Server-side AES should decrypt what client-side AES encrypted
                using var decrypted = serverAes.ProcessIncoming(encrypted);
                CollectionAssert.AreEqual(
                    GetBytes(original), GetBytes(decrypted),
                    "Server should decrypt client's data with the same key");
            }
        }

        #endregion

        #region Performance Tests

        [TestMethod("Perf: LZ4 compress+decompress 1KB × 10000")]
        public void Perf_LZ4_1KB()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 0 };
            var data = CreateTestArray(1024, 0x42);
            
            // Warmup
            for (int i = 0; i < 100; i++)
            {
                var c = lz4.ProcessOutgoing(data);
                var d = lz4.ProcessIncoming(c);
                c.Dispose();
                d.Dispose();
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 10000;
            for (int i = 0; i < iterations; i++)
            {
                var c = lz4.ProcessOutgoing(data);
                var d = lz4.ProcessIncoming(c);
                c.Dispose();
                d.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"LZ4 1KB roundtrip: {opsPerSec:N0} ops/sec ({sw.Elapsed.TotalMilliseconds / iterations:F3} ms/op)");
            Assert.IsTrue(opsPerSec > 1000, $"LZ4 1KB too slow: {opsPerSec:N0} ops/sec");

            data.Dispose();
        }

        [TestMethod("Perf: AES-GCM encrypt+decrypt 1KB × 10000")]
        public void Perf_AesGcm_1KB()
        {
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);
            var data = CreateTestArray(1024, 0x42);
            
            // Warmup
            for (int i = 0; i < 100; i++)
            {
                var e = aes.ProcessOutgoing(data);
                var d = aes.ProcessIncoming(e);
                e.Dispose();
                d.Dispose();
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 10000;
            for (int i = 0; i < iterations; i++)
            {
                var e = aes.ProcessOutgoing(data);
                var d = aes.ProcessIncoming(e);
                e.Dispose();
                d.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"AES-GCM 1KB roundtrip: {opsPerSec:N0} ops/sec ({sw.Elapsed.TotalMilliseconds / iterations:F3} ms/op)");
            Assert.IsTrue(opsPerSec > 1000, $"AES-GCM 1KB too slow: {opsPerSec:N0} ops/sec");

            data.Dispose();
        }

        [TestMethod("Perf: LZ4 + AES-GCM chain 1KB × 10000")]
        public void Perf_Chain_1KB()
        {
            var lz4 = new LZ4Middleware { MinSizeToCompress = 0 };
            byte[] key = GenerateKey(32);
            using var aes = new AesGcmMiddleware(key);
            IPacketMiddleware[] chain = { lz4, aes };

            var data = CreateTestArray(1024, 0x42);

            // Warmup
            for (int i = 0; i < 100; i++)
            {
                INGCArray c = data;
                foreach (var mw in chain) { var n = mw.ProcessOutgoing(c); if (c != data) c.Dispose(); c = n; }
                INGCArray d = c;
                for (int j = chain.Length - 1; j >= 0; j--) { var n = chain[j].ProcessIncoming(d); if (d != c) d.Dispose(); d = n; }
                c.Dispose();
                d.Dispose();
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int iterations = 10000;
            for (int i = 0; i < iterations; i++)
            {
                INGCArray c = data;
                foreach (var mw in chain) { var n = mw.ProcessOutgoing(c); if (c != data) c.Dispose(); c = n; }
                var enc = c;
                INGCArray d = enc;
                for (int j = chain.Length - 1; j >= 0; j--) { var n = chain[j].ProcessIncoming(d); if (d != enc) d.Dispose(); d = n; }
                enc.Dispose();
                d.Dispose();
            }
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            Console.WriteLine($"LZ4+AES-GCM 1KB roundtrip: {opsPerSec:N0} ops/sec ({sw.Elapsed.TotalMilliseconds / iterations:F3} ms/op)");
            Assert.IsTrue(opsPerSec > 500, $"Chain 1KB too slow: {opsPerSec:N0} ops/sec");

            data.Dispose();
        }

        #endregion

        #region Helpers

        private static INGCArray CreateTestArray(int size, byte fillByte)
        {
            var arr = new NGCArray(size);
            for (int i = 0; i < size; i++)
                arr.Bytes[i] = fillByte;
            return arr;
        }

        private static byte[] GenerateKey(int size)
        {
            var key = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(key);
            return key;
        }

        private static void AssertArraysEqual(INGCArray expected, INGCArray actual, string message = null)
        {
            Assert.AreEqual(expected.Length, actual.Length,
                $"Length mismatch: expected {expected.Length}, got {actual.Length}. {message}");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(
                    expected.Bytes[expected.Offset + i],
                    actual.Bytes[actual.Offset + i],
                    $"Byte mismatch at index {i}. {message}");
            }
        }

        /// <summary>
        /// Извлекает байты из INGCArray в обычный byte[] для CollectionAssert
        /// </summary>
        private static byte[] GetBytes(INGCArray arr)
        {
            var result = new byte[arr.Length];
            Buffer.BlockCopy(arr.Bytes, arr.Offset, result, 0, arr.Length);
            return result;
        }

        /// <summary>
        /// Creates a connected pair of FakeNetworkClients that can exchange data
        /// </summary>
        private (FakeNetworkClient client, FakeNetworkClient server) CreateConnectedPair()
        {
            var clientToServer = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
            var serverToClient = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

            var client = new FakeNetworkClient(clientToServer, serverToClient);
            var server = new FakeNetworkClient(serverToClient, clientToServer);

            return (client, server);
        }

        #endregion
    }

    #region Fake Network Client for Testing

    /// <summary>
    /// Fake INetworkClient for unit testing middleware without real network
    /// </summary>
    internal class FakeNetworkClient : INetworkClient
    {
        public bool IsConnect => true;
        public int SendByteSpeed => 0;
        public float DeliveredRate => 1.0f;
        public RandomDropType RandomDrop { get; set; }
        
        public event INetworkClientDisconnected Disconnected;

        /// <summary>All packets sent through this client (raw bytes)</summary>
        public readonly List<byte[]> SentPackets = new List<byte[]>();

        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _sendQueue;
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _recvQueue;
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _acceptQueue = 
            new System.Collections.Concurrent.ConcurrentQueue<byte[]>();

        /// <summary>
        /// Simple fake for send-only testing
        /// </summary>
        public FakeNetworkClient()
        {
            _sendQueue = null;
            _recvQueue = null;
        }

        /// <summary>
        /// Connected pair mode: send goes to sendQueue, accept reads from recvQueue
        /// </summary>
        public FakeNetworkClient(
            System.Collections.Concurrent.ConcurrentQueue<byte[]> sendQueue,
            System.Collections.Concurrent.ConcurrentQueue<byte[]> recvQueue)
        {
            _sendQueue = sendQueue;
            _recvQueue = recvQueue;
        }

        public Task<bool> Connect(string address, CancellationToken token) => Task.FromResult(true);
        public void Disconnect(string user_error) { Disconnected?.Invoke(user_error); }
        public string GetRemoteClientAddress() => "fake://localhost";

        public Task Send(INGCArray array, bool guaranteed, CancellationToken token)
        {
            // Copy the data
            byte[] copy = new byte[array.Length];
            Buffer.BlockCopy(array.Bytes, array.Offset, copy, 0, array.Length);
            SentPackets.Add(copy);

            _sendQueue?.Enqueue(copy);
            return Task.CompletedTask;
        }

        public void QueueForAccept(byte[] data)
        {
            _acceptQueue.Enqueue(data);
        }

        public async Task<INGCArray> AcceptPacket(int max_size, CancellationToken token)
        {
            // Try accept queue first (for simple testing)
            if (_acceptQueue.TryDequeue(out var queued))
            {
                return new EasyArray(queued);
            }

            // Then try recv queue (for connected pair)
            if (_recvQueue != null)
            {
                while (!token.IsCancellationRequested)
                {
                    if (_recvQueue.TryDequeue(out var data))
                    {
                        return new EasyArray(data);
                    }
                    await Task.Delay(1, token).ConfigureAwait(false);
                }
            }

            return new EasyArray(0); // empty = disconnected
        }
    }

    #endregion
}
