using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EMI.NetUDPv2;

namespace Test.NetUDPv2
{
    /// <summary>
    /// Тесты бинарной сериализации заголовков UDP v2.
    /// Проверяют write/read roundtrip для всех типов пакетов.
    /// </summary>
    [TestClass]
    public class Test_V2Headers
    {
        #region Primitive Read/Write

        [TestMethod]
        public void UInt16_Roundtrip()
        {
            var buf = new byte[16];
            ushort[] values = { 0, 1, 255, 256, ushort.MaxValue, 0x1234 };

            foreach (var v in values)
            {
                V2Headers.WriteUInt16(buf, 0, v);
                Assert.AreEqual(v, V2Headers.ReadUInt16(buf, 0), $"Failed for {v}");
            }
        }

        [TestMethod]
        public void UInt16_LittleEndian()
        {
            var buf = new byte[2];
            V2Headers.WriteUInt16(buf, 0, 0x0102);
            Assert.AreEqual(0x02, buf[0]); // low byte first
            Assert.AreEqual(0x01, buf[1]);
        }

        [TestMethod]
        public void UInt32_Roundtrip()
        {
            var buf = new byte[16];
            uint[] values = { 0, 1, 255, 65535, uint.MaxValue, 0x12345678 };

            foreach (var v in values)
            {
                V2Headers.WriteUInt32(buf, 0, v);
                Assert.AreEqual(v, V2Headers.ReadUInt32(buf, 0), $"Failed for {v}");
            }
        }

        [TestMethod]
        public void UInt32_LittleEndian()
        {
            var buf = new byte[4];
            V2Headers.WriteUInt32(buf, 0, 0x01020304);
            Assert.AreEqual(0x04, buf[0]);
            Assert.AreEqual(0x03, buf[1]);
            Assert.AreEqual(0x02, buf[2]);
            Assert.AreEqual(0x01, buf[3]);
        }

        [TestMethod]
        public void UInt64_Roundtrip()
        {
            var buf = new byte[16];
            ulong[] values = { 0, 1, ulong.MaxValue, 0x0102030405060708 };

            foreach (var v in values)
            {
                V2Headers.WriteUInt64(buf, 0, v);
                Assert.AreEqual(v, V2Headers.ReadUInt64(buf, 0), $"Failed for {v}");
            }
        }

        [TestMethod]
        public void UInt64_LittleEndian()
        {
            var buf = new byte[8];
            V2Headers.WriteUInt64(buf, 0, 0x0102030405060708);
            Assert.AreEqual(0x08, buf[0]);
            Assert.AreEqual(0x07, buf[1]);
            Assert.AreEqual(0x06, buf[2]);
            Assert.AreEqual(0x05, buf[3]);
            Assert.AreEqual(0x04, buf[4]);
            Assert.AreEqual(0x03, buf[5]);
            Assert.AreEqual(0x02, buf[6]);
            Assert.AreEqual(0x01, buf[7]);
        }

        [TestMethod]
        public void WriteAtOffset_DoesNotCorruptNeighborBytes()
        {
            var buf = new byte[16];
            for (int i = 0; i < buf.Length; i++) buf[i] = 0xFF;

            V2Headers.WriteUInt32(buf, 4, 0);

            // Соседние байты не должны измениться
            Assert.AreEqual(0xFF, buf[0]);
            Assert.AreEqual(0xFF, buf[3]);
            Assert.AreEqual(0xFF, buf[8]);
            // Written bytes should be 0
            Assert.AreEqual(0, buf[4]);
            Assert.AreEqual(0, buf[5]);
            Assert.AreEqual(0, buf[6]);
            Assert.AreEqual(0, buf[7]);
        }

        #endregion

        #region Reliable Header

        [TestMethod]
        public void ReliableHeader_Roundtrip()
        {
            var buf = new byte[16];

            V2Headers.WriteReliableHeader(buf, 0, V2PacketType.ReliableData, 42);
            V2Headers.ReadReliableHeader(buf, 0, out var type, out var seq);

            Assert.AreEqual(V2PacketType.ReliableData, type);
            Assert.AreEqual(42u, seq);
        }

        [TestMethod]
        public void ReliableHeader_MaxSeq()
        {
            var buf = new byte[16];

            V2Headers.WriteReliableHeader(buf, 0, V2PacketType.ReliableFragment, uint.MaxValue);
            V2Headers.ReadReliableHeader(buf, 0, out _, out var seq);

            Assert.AreEqual(uint.MaxValue, seq);
        }

        [TestMethod]
        public void ReliableHeader_Size()
        {
            Assert.AreEqual(5, V2Headers.ReliableHeaderSize);
        }

        #endregion

        #region Fragment Header

        [TestMethod]
        public void FragmentHeader_Roundtrip()
        {
            var buf = new byte[16];

            V2Headers.WriteFragmentHeader(buf, 0, 12345, 7, 100);
            V2Headers.ReadFragmentHeader(buf, 0, out var msgId, out var fragIdx, out var fragCount);

            Assert.AreEqual(12345u, msgId);
            Assert.AreEqual((ushort)7, fragIdx);
            Assert.AreEqual((ushort)100, fragCount);
        }

        [TestMethod]
        public void FragmentHeader_MaxValues()
        {
            var buf = new byte[16];

            V2Headers.WriteFragmentHeader(buf, 0, uint.MaxValue, ushort.MaxValue, ushort.MaxValue);
            V2Headers.ReadFragmentHeader(buf, 0, out var msgId, out var fragIdx, out var fragCount);

            Assert.AreEqual(uint.MaxValue, msgId);
            Assert.AreEqual(ushort.MaxValue, fragIdx);
            Assert.AreEqual(ushort.MaxValue, fragCount);
        }

        [TestMethod]
        public void FragmentHeader_Size()
        {
            Assert.AreEqual(8, V2Headers.FragmentHeaderSize);
        }

        #endregion

        #region ACK Packet

        [TestMethod]
        public void Ack_Roundtrip_NoSack()
        {
            var buf = new byte[64];

            int written = V2Headers.WriteAck(buf, 0, 100, 0xDEADBEEFCAFE0001, null, 0);
            Assert.AreEqual(V2Headers.AckBaseSize, written);

            V2Headers.ReadAck(buf, 0, written, out var ackSeq, out var ackBits, out var sack);

            Assert.AreEqual(100u, ackSeq);
            Assert.AreEqual(0xDEADBEEFCAFE0001UL, ackBits);
            Assert.IsNull(sack);
        }

        [TestMethod]
        public void Ack_Roundtrip_WithSack()
        {
            var buf = new byte[128];
            var sackRanges = new SackRange[]
            {
                new SackRange(10, 20),
                new SackRange(50, 75),
                new SackRange(100, 200),
            };

            int written = V2Headers.WriteAck(buf, 0, 500, ulong.MaxValue, sackRanges, 3);

            V2Headers.ReadAck(buf, 0, written, out var ackSeq, out var ackBits, out var readSack);

            Assert.AreEqual(500u, ackSeq);
            Assert.AreEqual(ulong.MaxValue, ackBits);
            Assert.IsNotNull(readSack);
            Assert.AreEqual(3, readSack.Length);
            Assert.AreEqual(10u, readSack[0].Start);
            Assert.AreEqual(20u, readSack[0].End);
            Assert.AreEqual(50u, readSack[1].Start);
            Assert.AreEqual(75u, readSack[1].End);
            Assert.AreEqual(100u, readSack[2].Start);
            Assert.AreEqual(200u, readSack[2].End);
        }

        [TestMethod]
        public void Ack_WrittenSize_IncludesSack()
        {
            var buf = new byte[128];
            var sack = new SackRange[] { new SackRange(1, 2) };

            int written = V2Headers.WriteAck(buf, 0, 0, 0, sack, 1);
            Assert.AreEqual(V2Headers.AckBaseSize + V2Headers.SackRangeSize, written);
        }

        [TestMethod]
        public void Ack_TypeByteIsCorrect()
        {
            var buf = new byte[64];
            V2Headers.WriteAck(buf, 0, 0, 0, null, 0);
            Assert.AreEqual((byte)V2PacketType.Ack, buf[0]);
        }

        #endregion

        #region Connection Request

        [TestMethod]
        public void ConnectionRequest_Roundtrip()
        {
            var buf = new byte[16];

            V2Headers.WriteConnectionRequest(buf, 0, V2Constants.ProtocolVersion, 0xCAFEBABE12345678);
            V2Headers.ReadConnectionRequest(buf, 0, out var version, out var token);

            Assert.AreEqual(V2Constants.ProtocolVersion, version);
            Assert.AreEqual(0xCAFEBABE12345678UL, token);
        }

        [TestMethod]
        public void ConnectionRequest_TypeByte()
        {
            var buf = new byte[16];
            V2Headers.WriteConnectionRequest(buf, 0, 1, 0);
            Assert.AreEqual((byte)V2PacketType.ConnectionRequest, buf[0]);
        }

        #endregion

        #region Connection Accept

        [TestMethod]
        public void ConnectionAccept_Roundtrip()
        {
            var buf = new byte[16];
            ulong token = 0xAABBCCDDEEFF0011;

            V2Headers.WriteConnectionAccept(buf, 0, token);
            var readToken = V2Headers.ReadConnectionAcceptToken(buf, 0);

            Assert.AreEqual(token, readToken);
        }

        [TestMethod]
        public void ConnectionAccept_TypeByte()
        {
            var buf = new byte[16];
            V2Headers.WriteConnectionAccept(buf, 0, 0);
            Assert.AreEqual((byte)V2PacketType.ConnectionAccept, buf[0]);
        }

        #endregion

        #region Ping / Pong

        [TestMethod]
        public void Ping_Roundtrip()
        {
            var buf = new byte[16];

            V2Headers.WritePing(buf, 0, 1234, 9876543210L);
            V2Headers.ReadPing(buf, 0, out var pingId, out var timestamp);

            Assert.AreEqual((ushort)1234, pingId);
            Assert.AreEqual(9876543210L, timestamp);
        }

        [TestMethod]
        public void Ping_TypeByte()
        {
            var buf = new byte[16];
            V2Headers.WritePing(buf, 0, 0, 0);
            Assert.AreEqual((byte)V2PacketType.Ping, buf[0]);
        }

        [TestMethod]
        public void Pong_TypeByte()
        {
            var buf = new byte[16];
            V2Headers.WritePong(buf, 0, 0, 0);
            Assert.AreEqual((byte)V2PacketType.Pong, buf[0]);
        }

        [TestMethod]
        public void Pong_EchoesTimestamp()
        {
            var buf = new byte[16];
            V2Headers.WritePong(buf, 0, 42, 1234567890L);
            V2Headers.ReadPing(buf, 0, out var pingId, out var ts);

            // Pong uses same layout as Ping (except type byte)
            Assert.AreEqual((ushort)42, pingId);
            Assert.AreEqual(1234567890L, ts);
        }

        #endregion

        #region NACK

        [TestMethod]
        public void Nack_Roundtrip()
        {
            var buf = new byte[128];
            var seqs = new uint[] { 10, 20, 30, 100, 500 };

            int written = V2Headers.WriteNack(buf, 0, seqs, seqs.Length);
            V2Headers.ReadNack(buf, 0, written, out var readSeqs);

            Assert.AreEqual(seqs.Length, readSeqs.Length);
            for (int i = 0; i < seqs.Length; i++)
                Assert.AreEqual(seqs[i], readSeqs[i]);
        }

        [TestMethod]
        public void Nack_TypeByte()
        {
            var buf = new byte[16];
            V2Headers.WriteNack(buf, 0, new uint[] { 1 }, 1);
            Assert.AreEqual((byte)V2PacketType.Nack, buf[0]);
        }

        [TestMethod]
        public void Nack_Empty()
        {
            var buf = new byte[16];
            int written = V2Headers.WriteNack(buf, 0, new uint[0], 0);
            Assert.AreEqual(V2Headers.NackBaseSize, written);

            V2Headers.ReadNack(buf, 0, written, out var seqs);
            Assert.AreEqual(0, seqs.Length);
        }

        [TestMethod]
        public void Nack_WrittenSize()
        {
            var buf = new byte[128];
            var seqs = new uint[] { 1, 2, 3 };

            int written = V2Headers.WriteNack(buf, 0, seqs, 3);
            Assert.AreEqual(V2Headers.NackBaseSize + 3 * 4, written);
        }

        [TestMethod]
        public void Nack_ClampedToMax()
        {
            var buf = new byte[1024];
            // Больше максимума — при чтении должно быть обрезано
            var seqs = new uint[V2Constants.MaxNackSequences + 10];
            for (int i = 0; i < seqs.Length; i++) seqs[i] = (uint)i;

            int written = V2Headers.WriteNack(buf, 0, seqs, seqs.Length);
            V2Headers.ReadNack(buf, 0, written, out var readSeqs);

            Assert.IsTrue(readSeqs.Length <= V2Constants.MaxNackSequences,
                $"ReadNack should clamp to {V2Constants.MaxNackSequences}, got {readSeqs.Length}");
        }

        #endregion

        #region Offset Independence

        [TestMethod]
        public void AllHeaders_WorkAtNonZeroOffset()
        {
            var buf = new byte[256];
            int off = 50;

            // Reliable header
            V2Headers.WriteReliableHeader(buf, off, V2PacketType.ReliableData, 999);
            V2Headers.ReadReliableHeader(buf, off, out var t, out var s);
            Assert.AreEqual(V2PacketType.ReliableData, t);
            Assert.AreEqual(999u, s);

            // Fragment header
            V2Headers.WriteFragmentHeader(buf, off, 7777, 3, 10);
            V2Headers.ReadFragmentHeader(buf, off, out var mId, out var fi, out var fc);
            Assert.AreEqual(7777u, mId);
            Assert.AreEqual((ushort)3, fi);
            Assert.AreEqual((ushort)10, fc);

            // Connection request
            V2Headers.WriteConnectionRequest(buf, off, 2, 12345);
            V2Headers.ReadConnectionRequest(buf, off, out var ver, out var tok);
            Assert.AreEqual((ushort)2, ver);
            Assert.AreEqual(12345UL, tok);
        }

        #endregion

        #region SackRange Structure

        [TestMethod]
        public void SackRange_Constructor()
        {
            var r = new SackRange(10, 20);
            Assert.AreEqual(10u, r.Start);
            Assert.AreEqual(20u, r.End);
        }

        [TestMethod]
        public void SackRange_ZeroRange()
        {
            var r = new SackRange(5, 5);
            Assert.AreEqual(r.Start, r.End);
        }

        #endregion
    }
}
