using EMI.Indicators;
using System;
using System.Collections.Generic;
using System.IO;

namespace EMI.NetStream
{
    /// <summary>
    /// Хост для раздачи файлов через <see cref="FileDownloader"/>.
    /// Создаёт <see cref="NetStreamHost"/> для каждого запрошенного файла.
    /// </summary>
    public class FilesHost : IDisposable
    {
        private readonly Client _client;
        private readonly Func<string, Stream> _getFile;
        private readonly IRPCRemoveHandle _rpcHandle;
        private readonly List<NetStreamHost> _openStreams = new List<NetStreamHost>();
        private readonly object _sync = new object();
        private bool _disposed;

        /// <summary>
        /// Создать хост для раздачи файлов.
        /// </summary>
        /// <param name="client">EMI-клиент</param>
        /// <param name="id">Идентификатор хоста (должен совпадать на стороне <see cref="FileDownloader"/>)</param>
        /// <param name="getFile">
        /// Функция, возвращающая <see cref="Stream"/> для указанного имени файла.
        /// Вернуть <c>null</c> если файл не найден.
        /// </param>
        public FilesHost(Client client, int id, Func<string, Stream> getFile)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _getFile = getFile ?? throw new ArgumentNullException(nameof(getFile));

            var indicator = new Indicator.FuncOut<(bool, int), string>("FileHost_GetFileIndicator_" + id);
            _rpcHandle = client.LocalRPC.RegisterMethod(GetFile, indicator);
        }

        private (bool, int) GetFile(string filePath)
        {
            Stream stream;
            try
            {
                stream = _getFile(filePath);
            }
            catch
            {
                return (false, -1);
            }

            if (stream == null)
                return (false, -1);

            var host = NetStreamHost.Create(_client, stream);

            lock (_sync)
            {
                _openStreams.Add(host);
            }

            return (true, host.ID);
        }

        /// <summary>
        /// Закрывает все открытые потоки и удаляет регистрацию RPC.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _rpcHandle.Remove(); } catch { }

            lock (_sync)
            {
                foreach (var host in _openStreams)
                {
                    try { host.Dispose(); } catch { }
                }
                _openStreams.Clear();
            }
        }
    }
}
