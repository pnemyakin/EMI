using System;
using System.Buffers;

namespace EMI.NGC
{
    /// <summary>
    /// Массив который будет сохраняться для частого реиспользования (снимает нагрузку с сборщика мусора) (обязательно вызывать Dispose).
    /// Внутренне использует <see cref="ArrayPool{T}.Shared"/> — lock-free, O(1) бакетированный пул.
    /// </summary>
    public struct NGCArray : INGCArray
    {
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

        /// <summary>
        /// [Устарело] Оставлено для совместимости с тестами. ArrayPool сам управляет временем жизни под давлением GC Gen2.
        /// </summary>
        public static TimeSpan ArrayLifetime = new TimeSpan(0, 1, 0);

        /// <summary>
        /// Сколько массивов используется [не учитываются массивы в ArrayPool]
        /// </summary>
        public static int UseArrays
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif

        /// <summary>
        /// Сумарный размер всех используемых массивов
        /// </summary>
        public static long TotalUseSize
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif

        /// <summary>
        /// [Устарело] ArrayPool не раскрывает количество свободных массивов. Всегда 0.
        /// </summary>
        public static int FreeArraysCount =>
#if DEBUG
            0;
#else
            throw new NotSupportedException();
#endif

        /// <summary>
        /// [Устарело] ArrayPool не раскрывает сумарный размер свободных массивов. Всегда 0.
        /// </summary>
        public static long TotalFreeArraysSize
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif

        /// <inheritdoc/>
        public int Offset { get; set; }
        /// <inheritdoc/>
        public int Length { get; private set; }
        /// <inheritdoc/>
        public byte[] Bytes { get; private set; }

        /// <summary>
        /// Берёт массив из ArrayPool (или создаёт новый, если пул пуст)
        /// </summary>
        /// <param name="size">нужный логический размер</param>
        public NGCArray(int size)
        {
            Offset = 0;
            Length = size;
            Bytes = Pool.Rent(size);
#if DEBUG
            UseArrays++;
            TotalUseSize += Bytes.Length;
#endif
        }

#if DEBUG
        /// <summary>
        /// Учёт массива (выделенного) в счётчике производительности
        /// </summary>
        private static void AddUseArray(int size)
        {
            UseArrays++;
            TotalUseSize += size;
        }

        /// <summary>
        /// Учёт массива (освобождённого) в счётчике производительности
        /// </summary>
        private static void RemoveUseArray(int size)
        {
            UseArrays--;
            TotalUseSize -= size;
        }
#endif

        /// <summary>
        /// Возвращает массив в ArrayPool. После вызова массив НЕЛЬЗЯ использовать.
        /// </summary>
        public void Dispose()
        {
            if (Bytes != null)
            {
#if DEBUG
                RemoveUseArray(Bytes.Length);
#endif
                Pool.Return(Bytes);
                Bytes = null;
            }
        }

        /// <summary>
        /// Сбрасывает DEBUG-счётчики (для тестов). ArrayPool очищается GC Gen2 — этот метод не влияет на пул.
        /// </summary>
        internal static void ClearPool()
        {
#if DEBUG
            TotalFreeArraysSize = 0;
            TotalUseSize = 0;
            UseArrays = 0;
#endif
        }
    }
}