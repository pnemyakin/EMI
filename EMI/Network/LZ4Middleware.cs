using System;
using EMI.NGC;

namespace EMI.Network
{
    /// <summary>
    /// Middleware сжатия LZ4. Пакеты меньше MinSizeToCompress не сжимаются.
    /// Формат: [1 байт флаг: 0=raw, 1=compressed] [4 байта original size (LE)] [данные]
    /// При flag=0: [0] [original data]
    /// При flag=1: [1] [original_size: 4 bytes] [compressed data]
    /// Использует NGCArray пул для буферов — минимальный GC impact.
    /// </summary>
    public class LZ4Middleware : IPacketMiddleware
    {
        /// <inheritdoc/>
        public byte Id => MiddlewareIds.LZ4;

        /// <inheritdoc/>
        public string Name => "LZ4";

        /// <inheritdoc/>
        public byte Version => 1;

        /// <summary>
        /// Минимальный размер пакета для сжатия. Пакеты меньше этого размера передаются как есть.
        /// По умолчанию 256 байт.
        /// </summary>
        public int MinSizeToCompress { get; set; } = 256;

        private const byte FLAG_RAW = 0;
        private const byte FLAG_COMPRESSED = 1;
        private const int HEADER_SIZE_COMPRESSED = 5; // flag(1) + originalSize(4)
        private const int HEADER_SIZE_RAW = 1; // flag(1)

        /// <inheritdoc/>
        public INGCArray ProcessOutgoing(INGCArray input)
        {
            int inputLen = input.Length;

            if (inputLen < MinSizeToCompress)
            {
                // Too small to compress — wrap with flag byte only
                var output = new NGCArray(HEADER_SIZE_RAW + inputLen);
                output.Bytes[0] = FLAG_RAW;
                Buffer.BlockCopy(input.Bytes, 0, output.Bytes, HEADER_SIZE_RAW, inputLen);
                return output;
            }

            // Try to compress using LZ4
            int maxCompressedSize = LZ4Codec.MaximumOutputSize(inputLen);
            var tempOutput = new NGCArray(HEADER_SIZE_COMPRESSED + maxCompressedSize);

            int compressedSize = LZ4Codec.Encode(
                input.Bytes, 0, inputLen,
                tempOutput.Bytes, HEADER_SIZE_COMPRESSED, maxCompressedSize);

            if (compressedSize <= 0 || compressedSize >= inputLen)
            {
                // Compression didn't help — send raw
                tempOutput.Dispose();
                var rawOutput = new NGCArray(HEADER_SIZE_RAW + inputLen);
                rawOutput.Bytes[0] = FLAG_RAW;
                Buffer.BlockCopy(input.Bytes, 0, rawOutput.Bytes, HEADER_SIZE_RAW, inputLen);
                return rawOutput;
            }

            // Compression succeeded
            tempOutput.Bytes[0] = FLAG_COMPRESSED;
            WriteInt32LE(tempOutput.Bytes, 1, inputLen);

            // Create right-sized output from pool
            int totalSize = HEADER_SIZE_COMPRESSED + compressedSize;
            if (tempOutput.Bytes.Length <= totalSize * 2)
            {
                // Temp buffer is close enough in size, reuse it
                // Shrink Length via a wrapper
                var finalOutput = new EasyArray(totalSize);
                Buffer.BlockCopy(tempOutput.Bytes, 0, finalOutput.Bytes, 0, totalSize);
                tempOutput.Dispose();
                return finalOutput;
            }
            else
            {
                // Copy to right-sized pool buffer
                var finalOutput = new NGCArray(totalSize);
                Buffer.BlockCopy(tempOutput.Bytes, 0, finalOutput.Bytes, 0, totalSize);
                tempOutput.Dispose();
                return finalOutput;
            }
        }

        /// <inheritdoc/>
        public INGCArray ProcessIncoming(INGCArray input)
        {
            int inputLen = input.Length - input.Offset;

            if (inputLen < 1)
                throw new InvalidOperationException("LZ4Middleware: packet too short (no flag byte)");

            byte flag = input.Bytes[input.Offset];

            if (flag == FLAG_RAW)
            {
                // Raw packet — strip flag byte
                int dataLen = inputLen - HEADER_SIZE_RAW;
                var output = new NGCArray(dataLen);
                Buffer.BlockCopy(input.Bytes, input.Offset + HEADER_SIZE_RAW, output.Bytes, 0, dataLen);
                return output;
            }

            if (flag == FLAG_COMPRESSED)
            {
                if (inputLen < HEADER_SIZE_COMPRESSED)
                    throw new InvalidOperationException("LZ4Middleware: compressed packet too short");

                int originalSize = ReadInt32LE(input.Bytes, input.Offset + 1);
                if (originalSize <= 0 || originalSize > 1024 * 1024 * 100) // 100 MB sanity check
                    throw new InvalidOperationException($"LZ4Middleware: invalid original size {originalSize}");

                var output = new NGCArray(originalSize);
                int decompressedSize = LZ4Codec.Decode(
                    input.Bytes, input.Offset + HEADER_SIZE_COMPRESSED, inputLen - HEADER_SIZE_COMPRESSED,
                    output.Bytes, 0, originalSize);

                if (decompressedSize != originalSize)
                {
                    output.Dispose();
                    throw new InvalidOperationException(
                        $"LZ4Middleware: decompressed size mismatch (expected {originalSize}, got {decompressedSize})");
                }

                return output;
            }

            throw new InvalidOperationException($"LZ4Middleware: unknown flag byte 0x{flag:X2}");
        }

        private static void WriteInt32LE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32LE(byte[] buf, int offset)
        {
            return buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24);
        }
    }

    /// <summary>
    /// Минимальный LZ4 кодек (block mode) — встроенная реализация без внешних зависимостей.
    /// Базируется на LZ4 block format specification.
    /// Для высоконагруженных сценариев рекомендуется заменить на K4os.Compression.LZ4.
    /// </summary>
    internal static class LZ4Codec
    {
        private const int MINMATCH = 4;
        private const int COPYLENGTH = 8;
        private const int ML_BITS = 4;
        private const int ML_MASK = (1 << ML_BITS) - 1;
        private const int RUN_BITS = 8 - ML_BITS;
        private const int RUN_MASK = (1 << RUN_BITS) - 1;
        private const int MFLIMIT = 12;
        private const int LASTLITERALS = 5;
        private const int MAXD_LOG = 16;
        private const int MAXD = 1 << MAXD_LOG;
        private const int HASHHC_LOG = 15;
        private const int HASHTABLESIZE = 1 << 14;

        public static int MaximumOutputSize(int inputSize)
        {
            return inputSize + (inputSize / 255) + 16;
        }

        public static int Encode(byte[] input, int inputOffset, int inputLength,
            byte[] output, int outputOffset, int outputMaxLength)
        {
            if (inputLength == 0) return 0;

            var hashTable = new int[HASHTABLESIZE];
            for (int i = 0; i < hashTable.Length; i++)
                hashTable[i] = -1;

            int iP = inputOffset;
            int iEnd = inputOffset + inputLength;
            int iLimit = iEnd - MFLIMIT;
            int iLastLit = iEnd - LASTLITERALS;
            int oP = outputOffset;
            int oEnd = outputOffset + outputMaxLength;
            int anchor = iP;

            if (inputLength < MFLIMIT)
            {
                // Too small — just emit literals
                return EmitLastLiterals(input, anchor, iEnd - anchor, output, oP, oEnd) - outputOffset;
            }

            iP++; // skip first byte

            while (true)
            {
                int forwardIP = iP;
                int step = 1;
                int searchMatchNb = (1 << 6);
                int refP;

                // Find a match
                do
                {
                    iP = forwardIP;
                    forwardIP += step;
                    step = (searchMatchNb++ >> 6);

                    if (forwardIP > iLimit)
                        return EmitLastLiterals(input, anchor, iEnd - anchor, output, oP, oEnd) - outputOffset;

                    int h = Hash(input, iP);
                    refP = hashTable[h];
                    hashTable[h] = iP;
                }
                while (refP < inputOffset || refP >= iP || iP - refP >= MAXD ||
                       input[refP] != input[iP] ||
                       input[refP + 1] != input[iP + 1] ||
                       input[refP + 2] != input[iP + 2] ||
                       input[refP + 3] != input[iP + 3]);

                // Catch up (extend match backwards)
                while (iP > anchor && refP > inputOffset && input[iP - 1] == input[refP - 1])
                {
                    iP--;
                    refP--;
                }

                // Encode Literal Run
                int litLength = iP - anchor;
                int tokenPos = oP++;
                if (oP >= oEnd) return -1;

                if (litLength >= RUN_MASK)
                {
                    output[tokenPos] = (byte)(RUN_MASK << ML_BITS);
                    int remaining = litLength - RUN_MASK;
                    while (remaining >= 255)
                    {
                        if (oP >= oEnd) return -1;
                        output[oP++] = 255;
                        remaining -= 255;
                    }
                    if (oP >= oEnd) return -1;
                    output[oP++] = (byte)remaining;
                }
                else
                {
                    output[tokenPos] = (byte)(litLength << ML_BITS);
                }

                // Copy literals
                if (oP + litLength > oEnd) return -1;
                Buffer.BlockCopy(input, anchor, output, oP, litLength);
                oP += litLength;

                // Encode Offset
                int offset = iP - refP;
                if (oP + 2 > oEnd) return -1;
                output[oP++] = (byte)(offset & 0xFF);
                output[oP++] = (byte)(offset >> 8);

                // Count match length
                iP += MINMATCH;
                refP += MINMATCH;
                int matchStart = iP;
                while (iP < iLastLit && input[iP] == input[refP])
                {
                    iP++;
                    refP++;
                }
                int matchLength = iP - matchStart;

                if (matchLength >= ML_MASK)
                {
                    output[tokenPos] |= (byte)ML_MASK;
                    int remaining = matchLength - ML_MASK;
                    while (remaining >= 255)
                    {
                        if (oP >= oEnd) return -1;
                        output[oP++] = 255;
                        remaining -= 255;
                    }
                    if (oP >= oEnd) return -1;
                    output[oP++] = (byte)remaining;
                }
                else
                {
                    output[tokenPos] |= (byte)matchLength;
                }

                anchor = iP;

                // Update hash
                if (iP <= iLimit)
                {
                    hashTable[Hash(input, iP - 2)] = iP - 2;
                    int h = Hash(input, iP);
                    hashTable[h] = iP;
                }
                else
                {
                    break;
                }
            }

            // Last literals
            return EmitLastLiterals(input, anchor, iEnd - anchor, output, oP, oEnd) - outputOffset;
        }

        public static int Decode(byte[] input, int inputOffset, int inputLength,
            byte[] output, int outputOffset, int outputLength)
        {
            int iP = inputOffset;
            int iEnd = inputOffset + inputLength;
            int oP = outputOffset;
            int oEnd = outputOffset + outputLength;

            while (true)
            {
                if (iP >= iEnd) break;
                int token = input[iP++];

                // Literal length
                int litLength = token >> ML_BITS;
                if (litLength == RUN_MASK)
                {
                    int s;
                    do
                    {
                        if (iP >= iEnd) return oP - outputOffset;
                        s = input[iP++];
                        litLength += s;
                    }
                    while (s == 255);
                }

                // Copy literals
                if (litLength > 0)
                {
                    if (iP + litLength > iEnd || oP + litLength > oEnd)
                        return oP - outputOffset;
                    Buffer.BlockCopy(input, iP, output, oP, litLength);
                    iP += litLength;
                    oP += litLength;
                }

                if (iP >= iEnd) break; // Input consumed after last literal block

                // Decode offset
                if (iP + 2 > iEnd) return oP - outputOffset;
                int offset = input[iP] | (input[iP + 1] << 8);
                iP += 2;
                if (offset == 0) return oP - outputOffset; // bad offset

                int matchPos = oP - offset;
                if (matchPos < outputOffset) return oP - outputOffset; // bad offset

                // Match length
                int matchLength = token & ML_MASK;
                if (matchLength == ML_MASK)
                {
                    int s;
                    do
                    {
                        if (iP >= iEnd) return oP - outputOffset;
                        s = input[iP++];
                        matchLength += s;
                    }
                    while (s == 255);
                }
                matchLength += MINMATCH;

                // Copy match
                if (oP + matchLength > oEnd) return oP - outputOffset;

                // Can't use BlockCopy for overlapping regions
                for (int i = 0; i < matchLength; i++)
                {
                    output[oP + i] = output[matchPos + i];
                }
                oP += matchLength;
            }

            return oP - outputOffset;
        }

        private static int Hash(byte[] data, int offset)
        {
            uint val = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            return (int)((val * 2654435761u) >> (32 - 14)) & (HASHTABLESIZE - 1);
        }

        private static int EmitLastLiterals(byte[] input, int anchor, int litLength,
            byte[] output, int oP, int oEnd)
        {
            if (litLength >= RUN_MASK)
            {
                if (oP >= oEnd) return -1;
                output[oP++] = (byte)(RUN_MASK << ML_BITS);
                int remaining = litLength - RUN_MASK;
                while (remaining >= 255)
                {
                    if (oP >= oEnd) return -1;
                    output[oP++] = 255;
                    remaining -= 255;
                }
                if (oP >= oEnd) return -1;
                output[oP++] = (byte)remaining;
            }
            else
            {
                if (oP >= oEnd) return -1;
                output[oP++] = (byte)(litLength << ML_BITS);
            }

            if (oP + litLength > oEnd) return -1;
            Buffer.BlockCopy(input, anchor, output, oP, litLength);
            oP += litLength;
            return oP;
        }
    }
}
