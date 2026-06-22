using EMI.Indicators;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EMI.NetStream
{
    /// <summary>
    /// Клиент для загрузки файлов с удалённого <see cref="FilesHost"/>.
    /// </summary>
    public static class FileDownloader
    {
        /// <summary>
        /// Информация о прогрессе загрузки.
        /// </summary>
        public struct DownloadProgress
        {
            /// <summary>Полный размер файла в байтах.</summary>
            public long TotalLength;
            /// <summary>Сколько байт уже загружено.</summary>
            public long DownloadedLength;
            /// <summary>Прогресс в процентах (0–100).</summary>
            public double Percent;

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"TotalLength: {TotalLength / 1024} KB | Downloaded: {DownloadedLength / 1024} KB | {Math.Round(Percent, 2)}%";
            }
        }

        /// <summary>
        /// Скачать файл по сети.
        /// </summary>
        /// <param name="client">EMI-клиент, подключённый к хосту</param>
        /// <param name="hostId">Идентификатор <see cref="FilesHost"/></param>
        /// <param name="fileName">Имя файла на хосте</param>
        /// <param name="destination">Поток для записи скачанного файла</param>
        /// <param name="progress">Колбэк прогресса (вызывается из async-контекста)</param>
        /// <param name="chunkSize">
        /// Размер одного RPC-чанка в байтах (4 МБ по умолчанию).
        /// Каждый чанк — один RPC-вызов, который на UDP-транспорте разбивается
        /// на MTU-фрагменты с надёжной доставкой. С SACK + delayed ACK + proportional CWND
        /// протокол стабильно держит 4 МБ ≈ 2900 фрагментов.
        /// </param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns><c>true</c> если файл найден и скачан; <c>false</c> если файл не существует на хосте</returns>
        public static async Task<bool> Download(
            Client client,
            int hostId,
            string fileName,
            Stream destination,
            Action<DownloadProgress> progress = null,
            int chunkSize = 4 * 1024 * 1024,
            CancellationToken cancellationToken = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            var fileHostIndicator = new Indicator.FuncOut<(bool, int), string>("FileHost_GetFileIndicator_" + hostId);
            var result = await fileHostIndicator.RCall(fileName, client, token: cancellationToken).ConfigureAwait(false);

            if (!result.Item1)
                return false;

            using (var remote = await NetStreamRemote.Open(client, result.Item2, cancellationToken).ConfigureAwait(false))
            {
                long totalLength = remote.Length;
                long position = remote.Position;

                // Перемотка в начало, если поток не на позиции 0
                if (position != 0)
                {
                    await remote.SetPositionAsync(0, cancellationToken).ConfigureAwait(false);
                    position = 0;
                }

                // Размер одного чанка для ReadAsync.
                // Каждый ReadAsync = один RPC-вызов → один большой ненадёжный блок фрагментов.
                // 4 МБ = ~3000 фрагментов: на интернете даже единичная потеря даёт шторм ретрансмиссий.
                // 512 КБ = ~372 фрагмента: fast-retransmit справляется до истечения таймаутов.
                // Overhead на RTT минимален: cwnd уже раскрыт после первых чанков.
                byte[] buffer = new byte[chunkSize];
                var info = new DownloadProgress { TotalLength = totalLength };

                while (position < totalLength)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int toRead = (int)Math.Min(chunkSize, totalLength - position);
                    int bytesRead = await remote.ReadAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);

                    if (bytesRead <= 0)
                        break; // Конец потока

                    await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    position = remote.Position;

                    info.DownloadedLength = position;
                    info.Percent = totalLength > 0 ? (double)position / totalLength * 100.0 : 100.0;

                    progress?.Invoke(info);
                }

                await remote.CloseAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
    }
}
