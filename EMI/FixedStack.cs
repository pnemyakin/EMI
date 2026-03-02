using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/*
 * 21.06.2022 - создано и полность отестировано в ручную
 * 2025 - переход с spin-wait (Task.Yield) на Channel<T>
 */

/// <summary>
/// Асинхронный потокобезопасный стек фиксированного размера (когда он заполнен или пуст - просто ожидает)
/// </summary>
/// <typeparam name="T">Тип который будет содержаться в стеке</typeparam>
internal class FixedStack<T>
{
    private readonly Channel<T> _channel;
    /// <summary>
    /// Сколько элементов находить в стеке
    /// </summary>
    public int Count => _channel.Reader.Count;
    /// <summary>
    /// Сколько максимум может находиться элементов в стеке
    /// </summary>
    public int Size { get; }
    /// <summary>
    /// Максимальное время ожидания до отмены операции <see cref="Pop(CancellationToken)"/>
    /// </summary>
    private readonly TimeSpan MaxWaitTime;

    /// <summary>
    /// Инициализировать новый стек
    /// </summary>
    /// <param name="size">размер стека</param>
    /// <param name="maxWaitTime">Максимальное время ожидания до отмены операции <see cref="Pop(CancellationToken)"/></param>
    public FixedStack(int size, TimeSpan maxWaitTime)
    {
        Size = size;
        MaxWaitTime = maxWaitTime;
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(size)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Добавляет элемент в конец стека элемент (если стек переполнен, будет ожидать)
    /// </summary>
    /// <param name="item">предмет который будет добавлен</param>
    /// <param name="token">токен отмены операции</param>
    public async Task Push(T item, CancellationToken token)
    {
        try
        {
            await _channel.Writer.WriteAsync(item, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Совместимость со старым поведением: при отмене — просто выход
        }
    }

    /// <summary>
    /// Извлекает и возвращает последний элемент из стека (если стек пуст, будет ожидать)
    /// </summary>
    /// <param name="token">токен отмены операции (вернёт default значение)</param>
    /// <returns>элемент из стека</returns>
    public async Task<T> Pop(CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(MaxWaitTime);
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }
}