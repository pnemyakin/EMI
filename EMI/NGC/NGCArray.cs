using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace EMI.NGC
{
    /// <summary>
    /// Массив которй будет сохраняться для частого реиспользования (снимает нагрузку с сборщика мусора) (обязательно вызывать Dispose)
    /// </summary>
    public struct NGCArray : INGCArray
    {
        /// <summary>
        /// Сколько будет жить массив если не используется
        /// </summary>
        public static TimeSpan ArrayLifetime = new TimeSpan(0, 1, 0);
        /// <summary>
        /// Массивы которые в данный момент не используются
        /// </summary>
        private static readonly List<(byte[], DateTime)> FreeArrays = new List<(byte[], DateTime)>(100);
        /// <summary>
        /// Запущена ли задача очистки массивов
        /// </summary>
        private static volatile bool CleaningTimer = false;

        /// <summary>
        /// Сколько массивов используется [<see cref=" FreeArrays"/> не учитываются]
        /// </summary>


        public static int UseArrays
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif
        /// <summary>
        /// Сумарный размер всех массивов [<see cref=" FreeArrays"/> не учитываются]
        /// </summary>
        public static long TotalUseSize
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif
        /// <summary>
        /// Сколько массивов готово к реиспользованию в <see cref=" FreeArrays"/>
        /// </summary>
        public static int FreeArraysCount =>
#if DEBUG
            FreeArrays.Count;
#else
            throw new NotSupportedException();
#endif
        /// <summary>
        /// Сумарный размер всех массивов для реиспользования в <see cref=" FreeArrays"/>
        /// </summary>
        public static long TotalFreeArraysSize
#if DEBUG
        { get; private set; } = 0;
#else
        { get => throw new NotSupportedException(); private set => throw new NotSupportedException(); }
#endif

        /// <summary>
        /// Смещение - от куда следует считывать
        /// </summary>
        public int Offset { get; set; }
        /// <summary>
        /// Размер массива
        /// </summary>
        public int Length { get; private set; }
        /// <summary>
        /// Массив (размер массива следует считывать из другого поля)
        /// </summary>
        public byte[] Bytes { get; private set; }

        /// <summary>
        /// Пытается найти подходящий массив или просто создаёт новый
        /// </summary>
        /// <param name="size"></param>
        public NGCArray(int size)
        {
            Offset = 0;
            Length = size;
            lock (FreeArrays)
            {
                int goodSize = int.MaxValue;
                int index = -1;
                for (int i = 0; i < FreeArrays.Count; i++)
                {
                    var arr = FreeArrays[i].Item1;
                    if (arr.Length < goodSize && arr.Length >= size && arr.Length < size * 10)
                    {
                        index = i;
                        if (arr.Length == size)
                            break;
                        goodSize = arr.Length;
                    }
                }
                if (index != -1)
                {
                    Bytes = FreeArrays[index].Item1;
                    FreeArrays.RemoveAt(index);
#if DEBUG
                    RemoveFreeArray(Bytes.Length);
                    AddUseArray(Bytes.Length);
#endif

                }
                else
                {
                    Bytes = new byte[size];
#if DEBUG
                    AddUseArray(size);
#endif
                }
            }
        }
#if DEBUG
        /// <summary>
        /// Учёт массива (выделенного) в счётчике производительности
        /// </summary>
        /// <param name="size">размер массива</param>
        private static void AddUseArray(int size)
        {
            UseArrays++;
            TotalUseSize += size;
        }
    
        /// <summary>
        /// Учёт массива (выделенного) в счётчике производительности
        /// </summary>
        /// <param name="size">размер массива</param>
        private static void RemoveUseArray(int size)
        {
            UseArrays--;
            TotalUseSize -= size;
        }

        /// <summary>
        /// Учёт массива (свободного) в счётчике производительности
        /// </summary>
        /// <param name="size">размер массива</param>
        private static void AddFreeArray(int size) => TotalFreeArraysSize += size;

        /// <summary>
        /// Учёт массива (свободного) в счётчике производительности
        /// </summary>
        /// <param name="size">размер массива</param>
        private static void RemoveFreeArray(int size) => TotalFreeArraysSize -= size;
#endif

        /// <summary>
        /// Освобождает ресурсы, иначе масив нельзя реиспользовать (необходимо вызвать)
        /// </summary>
        public void Dispose()
        {
            if (Bytes != null)
            {
                lock (FreeArrays)
                {
#if DEBUG
                    RemoveUseArray(Bytes.Length);
                    AddFreeArray(Bytes.Length);
#endif
                    FreeArrays.Add((Bytes, DateTime.UtcNow + ArrayLifetime));

                    Bytes = null;
                    if (!CleaningTimer)
                        _ = Cleaner();
                }
            }
        }

        /// <summary>
        /// Очищает пул свободных массивов (для тестов)
        /// </summary>
        internal static void ClearPool()
        {
            lock (FreeArrays)
            {
#if DEBUG
                TotalFreeArraysSize = 0;
                TotalUseSize = 0;
                UseArrays = 0;
#endif
                FreeArrays.Clear();
                CleaningTimer = false;
            }
        }

        /// <summary>
        /// Ждёт/Удаляет неиспользуемые массивы
        /// </summary>
        private static async Task Cleaner()
        {
            CleaningTimer = true;
            while (true)
            {
                await Task.Delay(ArrayLifetime).ConfigureAwait(false);
                lock (FreeArrays)
                {
                    for (int i = 0; i < FreeArrays.Count; i++)
                    {
                        if ((FreeArrays[i].Item2 - DateTime.UtcNow).Ticks < 0)
                        {
#if DEBUG
                            RemoveFreeArray(FreeArrays[i].Item1.Length);
#endif
                            FreeArrays.RemoveAt(i--);
                        }
                    }

                    if (FreeArrays.Count == 0)
                    {
                        CleaningTimer = false;
                        break;
                    }
                }
            }
        }
    }
}