using System;
using SmartPackager;
using SmartPackager.ByteStack;

namespace EMI.NGC
{
    /// <summary>
    /// Кастомный упаковщик SmartPackager для <see cref="RawBuffer"/>.
    /// Автоматически обнаруживается через <c>PackMethods.SetupPackMethods</c> (сканирование сборок).
    /// </summary>
    /// <remarks>
    /// Формат на wire: [length: int] [raw bytes...].
    /// На принимающей стороне данные читаются напрямую в <see cref="NGCArray"/> из пула <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
    /// </remarks>
    internal class PackRawBuffer : IPackagerMethod<RawBuffer>
    {
        public Type TargetType => typeof(RawBuffer);
        public bool IsFixedSize => false;

        public void GetSize(ref StackMeter meter, RawBuffer source)
        {
            meter.AddLength();                // префикс длины (int)
            meter.Add<byte>(source.Length);   // сырые байты
        }

        public unsafe void PackUP(ref StackWriter writer, RawBuffer source)
        {
            writer.WriteLength(source.Length);
            if (source.Length > 0)
            {
                byte[] bytes = source.Bytes;
                int offset = source.Offset;
                fixed (byte* ptr = &bytes[offset])
                    writer.Write((IntPtr*)ptr, source.Length);
            }
        }

        public unsafe void UnPack(ref StackReader reader, out RawBuffer destination)
        {
            int len = reader.ReadLength();
            if (len <= 0)
            {
                destination = new RawBuffer(0);
            }
            else
            {
                var buf = new RawBuffer(len);
                byte[] bytes = buf.Bytes;
                int offset = buf.Offset;
                fixed (byte* ptr = &bytes[offset])
                    reader.Read((IntPtr*)ptr, len);
                destination = buf;
            }
        }
    }
}
