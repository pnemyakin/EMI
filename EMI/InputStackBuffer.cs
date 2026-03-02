using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/*
 * 21.06.2022 - создано, тестирование - юнит тесты
 */
namespace EMI
{
    using NGC;
    /// <summary>
    /// [не потокобезопасно] Сохраняет пакеты до их распаковки и обработки а так же имеет защиту от избыточного колличесва байт в стеке
    /// </summary>
    internal class InputStackBuffer
    {
        /// <summary>
        /// Стек элементов
        /// </summary>
        private readonly FixedStack<INGCArray> Stack;
        /// <summary>
        /// Общий лимит в байтах для стека
        /// </summary>
        private readonly int MaxBytesCount;
        /// <summary>
        /// Сколько было использованно байт стеком (может быть больше <see cref="MaxBytesCount"/>)
        /// </summary>
        private volatile int BytesCount = 0;

        /// <summary>
        /// Сколько стек вмещает элементов
        /// </summary>
        public int Size => Stack.Size;
        /// <summary>
        /// Сколько элементов в стеке
        /// </summary>
        public int Count => Stack.Count;
        /// <summary>
        /// Общий лимит в байтах для стека
        /// </summary>
        public int Capacity => MaxBytesCount;
        /// <summary>
        /// Сколько было использованно байт стеком (может быть больше <see cref="Capacity"/>)
        /// </summary>
        public int OccupiedCapacity => BytesCount;

        /// <summary>
        /// Инициализирует новый стек
        /// </summary>
        /// <param name="size">сколько элементов максимум может храниться в стеке (массивов)</param>
        /// <param name="capacity">сумарный размер массивов в байтах, который им разрешено занимать [иначе операция <see cref="Push(INGCArray, CancellationToken)"/> будет ожидать]</param>
        public InputStackBuffer(int size, int capacity)
        {
            Stack = new FixedStack<INGCArray>(size, new TimeSpan(0, 0, 1));
            MaxBytesCount = capacity;
        }

        /// <summary>
        /// Положить в стек массив (если в стеке превышен лимит вместимости, операция будет ожидать освобождения стека) [если лимит не превышен в стек может быть добавлен элемент размер которого превышает лимит]
        /// </summary>
        /// <param name="array">массив для добавления в стек</param>
        /// <param name="token">токен отмены операции</param>
        public async Task Push(INGCArray array, CancellationToken token)
        {
            // Ждём пока освободится место по байтам
            while (BytesCount >= MaxBytesCount && !token.IsCancellationRequested)
            {
                try { await Task.Delay(1, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            if (token.IsCancellationRequested) return;
            lock (this)
            {
                BytesCount += array.Bytes.Length;
            }
            await Stack.Push(array, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
                lock (this)
                {
                    BytesCount -= array.Bytes.Length;
                }
        }

        /// <summary>
        /// Извлекает <see cref="Handle"/> для использования массива [лимит освободиться только после вызова <see cref="Handle.Dispose"/>]>
        /// </summary>
        /// <param name="token">токен отмены операции, при отмене операции массив не освобождается [желательно вызвать для безопасности <see cref="Handle.Dispose"/>]</param>
        /// <returns></returns>
        public async Task<Handle> Pop(CancellationToken token)
        {
            return new Handle(this, await Stack.Pop(token).ConfigureAwait(false));
        }

        /// <summary>
        /// Хранит в себе полученный массив из стека, нужен что бы освободить лимит в стеке после окончания использования массива
        /// </summary>
        public struct Handle : IDisposable
        {
            private InputStackBuffer MyStackBuffer;
            public readonly INGCArray Buffer;

            internal Handle(InputStackBuffer myStackBuffer, INGCArray buffer)
            {
                MyStackBuffer = myStackBuffer;
                Buffer = buffer;
            }

            /// <summary>
            /// Освобождает лимит буфера, который порадил этот <see cref="Handle"/>, а так же освобождает массив
            /// </summary>
            public void Dispose()
            {
                if (MyStackBuffer != null)
                {
                    lock (MyStackBuffer)
                    {
                        MyStackBuffer.BytesCount -= Buffer.Bytes.Length;
                    }
                    MyStackBuffer = null;
                }
                Buffer?.Dispose();
            }
        }
    }
}