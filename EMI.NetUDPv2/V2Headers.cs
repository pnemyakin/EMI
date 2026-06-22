using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EMI.NetUDPv2
{
    /// <summary>
    /// Заголовки бинарных пакетов UDPv2. Все структуры little-endian, packed.
    /// Используем uint для sequence (4 млрд номеров вместо 65к у v1).
    /// </summary>
    internal static class V2Headers
    {
        // ── Sizes ──────────────────────────────────────────────────────────────

        /// <summary>Reliable header: [type:1][seq:4] = 5 байт</summary>
        public const int ReliableHeaderSize = 5;

        /// <summary>Fragment header: [msgId:4][fragIdx:2][fragCount:2] = 8 байт</summary>
        public const int FragmentHeaderSize = 8;

        /// <summary>ACK base: [type:1][ackSeq:4][ackBits:8][sackCount:1] = 14 байт</summary>
        public const int AckBaseSize = 14;

        /// <summary>SACK range: [start:4][end:4] = 8 байт</summary>
        public const int SackRangeSize = 8;

        /// <summary>Connection packet: [type:1][version:2][token:8] = 11 байт</summary>
        public const int ConnectionRequestSize = 11;

        /// <summary>Connection accept: [type:1][token:8] = 9 байт</summary>
        public const int ConnectionAcceptSize = 9;

        /// <summary>Ping/Pong: [type:1][pingId:2][timestamp:8] = 11 байт</summary>
        public const int PingSize = 11;

        /// <summary>NACK base: [type:1][count:2] = 3, + count*4 для seq</summary>
        public const int NackBaseSize = 3;

        // ── Write Helpers ──────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64(byte[] buf, int offset, ulong value)
        {
            buf[offset] = (byte)value;
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
            buf[offset + 4] = (byte)(value >> 32);
            buf[offset + 5] = (byte)(value >> 40);
            buf[offset + 6] = (byte)(value >> 48);
            buf[offset + 7] = (byte)(value >> 56);
        }

        // ── Read Helpers ───────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16(byte[] buf, int offset)
        {
            return (ushort)(buf[offset] | (buf[offset + 1] << 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32(byte[] buf, int offset)
        {
            return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadUInt64(byte[] buf, int offset)
        {
            return (ulong)buf[offset]
                | ((ulong)buf[offset + 1] << 8)
                | ((ulong)buf[offset + 2] << 16)
                | ((ulong)buf[offset + 3] << 24)
                | ((ulong)buf[offset + 4] << 32)
                | ((ulong)buf[offset + 5] << 40)
                | ((ulong)buf[offset + 6] << 48)
                | ((ulong)buf[offset + 7] << 56);
        }

        // ── Reliable Header ────────────────────────────────────────────────────

        /// <summary>
        /// Пишет заголовок надёжного пакета: [type:1][seq:4]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteReliableHeader(byte[] buf, int offset, V2PacketType type, uint seq)
        {
            buf[offset] = (byte)type;
            WriteUInt32(buf, offset + 1, seq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadReliableHeader(byte[] buf, int offset, out V2PacketType type, out uint seq)
        {
            type = (V2PacketType)buf[offset];
            seq = ReadUInt32(buf, offset + 1);
        }

        // ── Fragment Header ────────────────────────────────────────────────────

        /// <summary>
        /// Пишет заголовок фрагмента: [msgId:4][fragIdx:2][fragCount:2]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFragmentHeader(byte[] buf, int offset, uint msgId, ushort fragIdx, ushort fragCount)
        {
            WriteUInt32(buf, offset, msgId);
            WriteUInt16(buf, offset + 4, fragIdx);
            WriteUInt16(buf, offset + 6, fragCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadFragmentHeader(byte[] buf, int offset, out uint msgId, out ushort fragIdx, out ushort fragCount)
        {
            msgId = ReadUInt32(buf, offset);
            fragIdx = ReadUInt16(buf, offset + 4);
            fragCount = ReadUInt16(buf, offset + 6);
        }

        // ── ACK Packet ─────────────────────────────────────────────────────────

        /// <summary>
        /// Пишет ACK пакет: [type:1][ackSeq:4][ackBits:8][sackCount:1]
        /// Длина = AckBaseSize + sackCount * SackRangeSize
        /// </summary>
        public static int WriteAck(byte[] buf, int offset, uint ackSeq, ulong ackBits, SackRange[] sackRanges, int sackCount)
        {
            buf[offset] = (byte)V2PacketType.Ack;
            WriteUInt32(buf, offset + 1, ackSeq);
            WriteUInt64(buf, offset + 5, ackBits);
            buf[offset + 13] = (byte)sackCount;

            int pos = offset + AckBaseSize;
            for (int i = 0; i < sackCount; i++)
            {
                WriteUInt32(buf, pos, sackRanges[i].Start);
                WriteUInt32(buf, pos + 4, sackRanges[i].End);
                pos += SackRangeSize;
            }
            return pos - offset;
        }

        public static void ReadAck(byte[] buf, int offset, int length,
            out uint ackSeq, out ulong ackBits, out SackRange[] sackRanges)
        {
            ackSeq = ReadUInt32(buf, offset + 1);
            ackBits = ReadUInt64(buf, offset + 5);
            int sackCount = buf[offset + 13];

            sackRanges = null;
            if (sackCount > 0)
            {
                int maxRanges = Math.Min(sackCount, V2Constants.MaxSackRanges);
                int available = (length - AckBaseSize) / SackRangeSize;
                int count = Math.Min(maxRanges, available);

                sackRanges = new SackRange[count];
                int pos = offset + AckBaseSize;
                for (int i = 0; i < count; i++)
                {
                    sackRanges[i] = new SackRange(ReadUInt32(buf, pos), ReadUInt32(buf, pos + 4));
                    pos += SackRangeSize;
                }
            }
        }

        // ── Connection Request ─────────────────────────────────────────────────

        public static void WriteConnectionRequest(byte[] buf, int offset, ushort version, ulong token)
        {
            buf[offset] = (byte)V2PacketType.ConnectionRequest;
            WriteUInt16(buf, offset + 1, version);
            WriteUInt64(buf, offset + 3, token);
        }

        public static void ReadConnectionRequest(byte[] buf, int offset, out ushort version, out ulong token)
        {
            version = ReadUInt16(buf, offset + 1);
            token = ReadUInt64(buf, offset + 3);
        }

        // ── Connection Accept ──────────────────────────────────────────────────

        public static void WriteConnectionAccept(byte[] buf, int offset, ulong token)
        {
            buf[offset] = (byte)V2PacketType.ConnectionAccept;
            WriteUInt64(buf, offset + 1, token);
        }

        public static ulong ReadConnectionAcceptToken(byte[] buf, int offset)
        {
            return ReadUInt64(buf, offset + 1);
        }

        // ── Ping / Pong ────────────────────────────────────────────────────────

        public static void WritePing(byte[] buf, int offset, ushort pingId, long timestamp)
        {
            buf[offset] = (byte)V2PacketType.Ping;
            WriteUInt16(buf, offset + 1, pingId);
            WriteUInt64(buf, offset + 3, (ulong)timestamp);
        }

        public static void WritePong(byte[] buf, int offset, ushort pingId, long echoTimestamp)
        {
            buf[offset] = (byte)V2PacketType.Pong;
            WriteUInt16(buf, offset + 1, pingId);
            WriteUInt64(buf, offset + 3, (ulong)echoTimestamp);
        }

        public static void ReadPing(byte[] buf, int offset, out ushort pingId, out long timestamp)
        {
            pingId = ReadUInt16(buf, offset + 1);
            timestamp = (long)ReadUInt64(buf, offset + 3);
        }

        // ── NACK ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Пишет NACK пакет: [type:1][count:2][seq0:4][seq1:4]...
        /// </summary>
        public static int WriteNack(byte[] buf, int offset, uint[] sequences, int count)
        {
            buf[offset] = (byte)V2PacketType.Nack;
            WriteUInt16(buf, offset + 1, (ushort)count);
            int pos = offset + NackBaseSize;
            for (int i = 0; i < count; i++)
            {
                WriteUInt32(buf, pos, sequences[i]);
                pos += 4;
            }
            return pos - offset;
        }

        public static void ReadNack(byte[] buf, int offset, int length, out uint[] sequences)
        {
            int count = ReadUInt16(buf, offset + 1);
            int available = (length - NackBaseSize) / 4;
            count = Math.Min(count, available);
            count = Math.Min(count, V2Constants.MaxNackSequences);

            sequences = new uint[count];
            int pos = offset + NackBaseSize;
            for (int i = 0; i < count; i++)
            {
                sequences[i] = ReadUInt32(buf, pos);
                pos += 4;
            }
        }
    }

    /// <summary>
    /// Диапазон подтверждённых sequence для SACK
    /// </summary>
    internal struct SackRange
    {
        public uint Start;
        public uint End;

        public SackRange(uint start, uint end)
        {
            Start = start;
            End = end;
        }
    }
}
