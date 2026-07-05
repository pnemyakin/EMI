using System;
using System.Buffers;

namespace EMI.NGC
{
    /// <summary>
    /// Специальная структура для прямой передачи сырых байтов через RPC без сериализации SmartPackager.
    /// Под капотом использует <see cref="NGCArray"/> (пул <see cref="ArrayPool{T}.Shared"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Отправка:</b> выделите буфер через <c>new RawBuffer(size)</c>, наполните данными,
    /// передайте в RPC-вызов. Данные будут скопированы напрямую в транспортный буфер (без SmartPackager).</para>
    /// <para><b>Получение:</b> в обработчике работайте с буфером через <see cref="Bytes"/>/<see cref="Offset"/>/<see cref="Length"/>.
    /// По завершении обработчика буфер автоматически освобождается (возвращается в пул).</para>
    /// <para><b>Важно:</b> структура владеет ресурсом. Не копируйте её — используйте <c>using</c>.
    /// При копировании Dispose должен вызываться только на одной копии, иначе будет двойное освобождение.</para>
    /// </remarks>
    public struct RawBuffer : IDisposable
    {
        private INGCArray _array;
        private bool _disposed;

        /// <summary>
        /// Массив байт
        /// </summary>
        public readonly byte[] Bytes => _array?.Bytes;

        /// <summary>
        /// Смещение, с которого начинаются полезные данные
        /// </summary>
        public readonly int Offset => _array?.Offset ?? 0;

        /// <summary>
        /// Длина полезных данных
        /// </summary>
        public readonly int Length => _array?.Length ?? 0;

        /// <summary>
        /// Является ли буфер пустым
        /// </summary>
        public readonly bool IsEmpty => _array == null || _array.Bytes == null || _array.Length == 0;

        /// <summary>
        /// Span байт полезных данных
        /// </summary>
        public readonly Span<byte> Span
        {
            get
            {
                if (_array == null || _array.Bytes == null)
                    return Span<byte>.Empty;
                return new Span<byte>(_array.Bytes, _array.Offset, _array.Length);
            }
        }

        /// <summary>
        /// Memory байт полезных данных
        /// </summary>
        public readonly Memory<byte> Memory
        {
            get
            {
                if (_array == null || _array.Bytes == null)
                    return Memory<byte>.Empty;
                return new Memory<byte>(_array.Bytes, _array.Offset, _array.Length);
            }
        }

        /// <summary>
        /// Выделить новый буфер заданного размера из пула (<see cref="ArrayPool{T}.Shared"/>).
        /// Используйте для отправки данных.
        /// </summary>
        /// <param name="size">Размер буфера в байтах</param>
        public RawBuffer(int size)
        {
            _array = new NGCArray(size);
            _disposed = false;
        }

        /// <summary>
        /// Обернуть существующий INGCArray (для внутреннего использования на принимающей стороне).
        /// </summary>
        internal RawBuffer(INGCArray array)
        {
            _array = array;
            _disposed = false;
        }

        /// <summary>
        /// Создать копию данных в новом буфере из пула.
        /// Используется на принимающей стороне для передачи данных обработчику.
        /// </summary>
        /// <param name="source">Исходный массив</param>
        /// <param name="offset">Смещение в исходном массиве</param>
        /// <param name="length">Длина копируемых данных</param>
        internal static RawBuffer CopyFrom(byte[] source, int offset, int length)
        {
            if (source == null || length <= 0)
                return new RawBuffer(0);

            var buffer = new RawBuffer(length);
            Buffer.BlockCopy(source, offset, buffer.Bytes, buffer.Offset, length);
            return buffer;
        }

        /// <summary>
        /// Освобождает ресурсы — возвращает нижележащий массив в пул <see cref="ArrayPool{T}.Shared"/>.
        /// После вызова структуру использовать нельзя.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed && _array != null)
            {
                _array.Dispose();
                _array = null;
                _disposed = true;
            }
        }

        /// <inheritdoc/>
        public override readonly string ToString()
        {
            return _array == null ? "RawBuffer(disposed)" : $"RawBuffer({Length} bytes)";
        }
    }
}
