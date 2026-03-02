using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EMI.NetUDP
{
    /// <summary>
    /// Заголовок надёжного пакета: [type:1][seq:2] = 3 байта
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = SizeOf)]
    internal struct UDPReliableHeader
    {
        public const int SizeOf = 3;

        [FieldOffset(0)]
        public UDPPacketType Type;

        [FieldOffset(1)]
        public ushort Sequence;

        public UDPReliableHeader(UDPPacketType type, ushort sequence)
        {
            Type = type;
            Sequence = sequence;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static UDPReliableHeader FromBytes(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                return *(UDPReliableHeader*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteToBuffer(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                *((UDPReliableHeader*)ptr) = this;
            }
        }
    }

    /// <summary>
    /// Заголовок фрагмента: [fragId:2][fragIdx:2][fragCount:2] = 6 байт
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = SizeOf)]
    internal struct UDPFragmentHeader
    {
        public const int SizeOf = 6;

        /// <summary>
        /// Идентификатор исходного сообщения
        /// </summary>
        [FieldOffset(0)]
        public ushort FragmentID;

        /// <summary>
        /// Индекс фрагмента (0-based)
        /// </summary>
        [FieldOffset(2)]
        public ushort FragmentIndex;

        /// <summary>
        /// Общее кол-во фрагментов
        /// </summary>
        [FieldOffset(4)]
        public ushort FragmentCount;

        public UDPFragmentHeader(ushort fragmentID, ushort fragmentIndex, ushort fragmentCount)
        {
            FragmentID = fragmentID;
            FragmentIndex = fragmentIndex;
            FragmentCount = fragmentCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static UDPFragmentHeader FromBytes(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                return *(UDPFragmentHeader*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteToBuffer(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                *((UDPFragmentHeader*)ptr) = this;
            }
        }
    }

    /// <summary>
    /// Пакет подтверждения: [type:1][ackSeq:2][ackBits:4] = 7 байт
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = SizeOf)]
    internal struct UDPAckPacket
    {
        public const int SizeOf = 7;

        [FieldOffset(0)]
        public UDPPacketType Type;

        /// <summary>
        /// Номер последнего подтверждённого пакета
        /// </summary>
        [FieldOffset(1)]
        public ushort Sequence;

        /// <summary>
        /// Битовое поле: бит N = подтверждён ли пакет (Sequence - 1 - N)
        /// </summary>
        [FieldOffset(3)]
        public uint AckBitfield;

        public UDPAckPacket(ushort sequence, uint ackBitfield)
        {
            Type = UDPPacketType.Ack;
            Sequence = sequence;
            AckBitfield = ackBitfield;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static UDPAckPacket FromBytes(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                return *(UDPAckPacket*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteToBuffer(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                *((UDPAckPacket*)ptr) = this;
            }
        }
    }

    /// <summary>
    /// Пакет подключения/ответа: [type:1][token:8] = 9 байт
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = SizeOf)]
    internal struct UDPConnectionPacket
    {
        public const int SizeOf = 9;

        [FieldOffset(0)]
        public UDPPacketType Type;

        /// <summary>
        /// Токен для идентификации подключения
        /// </summary>
        [FieldOffset(1)]
        public ulong Token;

        public UDPConnectionPacket(UDPPacketType type, ulong token)
        {
            Type = type;
            Token = token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static UDPConnectionPacket FromBytes(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                return *(UDPConnectionPacket*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteToBuffer(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                *((UDPConnectionPacket*)ptr) = this;
            }
        }
    }

    /// <summary>
    /// Пакет пинга: [type:1][pingId:2] = 3 байта
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = SizeOf)]
    internal struct UDPPingPacket
    {
        public const int SizeOf = 3;

        [FieldOffset(0)]
        public UDPPacketType Type;

        [FieldOffset(1)]
        public ushort PingID;

        public UDPPingPacket(UDPPacketType type, ushort pingId)
        {
            Type = type;
            PingID = pingId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static UDPPingPacket FromBytes(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                return *(UDPPingPacket*)ptr;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteToBuffer(byte[] buffer, int offset = 0)
        {
            fixed (byte* ptr = &buffer[offset])
            {
                *((UDPPingPacket*)ptr) = this;
            }
        }
    }
}
