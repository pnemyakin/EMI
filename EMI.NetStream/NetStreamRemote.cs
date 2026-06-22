using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace EMI.NetStream
{
    using Structures;

    /// <summary>
    /// Удалённый поток — прокси над <see cref="Stream"/>, расположенным на другой стороне соединения.
    /// Все операции выполняются через RPC-вызовы поверх EMI.
    /// </summary>
    public class NetStreamRemote : Stream
    {
        private readonly Client _client;
        private readonly NetStreamIndicators _indicators;
        private NetStreamInfo _streamInfo;
        private long _length = -1;
        private long _position = -1;
        private bool _disposed;

        /// <inheritdoc/>
        public override bool CanRead => _streamInfo.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => _streamInfo.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => _streamInfo.CanWrite;

        /// <inheritdoc/>
        public override long Length
        {
            get
            {
                if (_length < 0)
                    throw new NotSupportedException("Remote stream does not support Length.");
                return _length;
            }
        }

        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                if (_position < 0)
                    throw new NotSupportedException("Remote stream does not support Position.");
                return _position;
            }
            set
            {
                SetPositionAsync(value, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Асинхронно устанавливает позицию удалённого потока.
        /// </summary>
        public async Task SetPositionAsync(long value, CancellationToken cancellationToken = default)
        {
            if (_position < 0)
                throw new NotSupportedException("Remote stream does not support Position.");

            bool result = await _indicators.SetStreamPosition.RCall(value, _client, token: cancellationToken).ConfigureAwait(false);
            if (!result)
                throw new IOException("Failed to set remote stream position.");
            _position = value;
        }

        #region Synchronous overrides (delegate to async)

        /// <inheritdoc/>
        public override void Flush()
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            return SeekAsync(offset, origin, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            SetLengthAsync(value, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        #endregion

        #region Async overrides

        /// <inheritdoc/>
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            var result = await _indicators.Flush.RCall(_client, token: cancellationToken).ConfigureAwait(false);
            if (!result.Result)
                throw new IOException("Failed to flush remote stream.");
            _length = result.Length;
            _position = result.Position;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("The sum of offset and count exceeds the buffer length.");

            var result = await _indicators.Read.RCall(new ReadInInfo(buffer.Length, offset, count), _client, token: cancellationToken).ConfigureAwait(false);

            if (!result.Result)
                throw new IOException("Failed to read from remote stream.");

            if (result.Buffer != null && result.ReadLen > 0)
            {
                int copyLen = Math.Min(result.ReadLen, result.Buffer.Length);
                Buffer.BlockCopy(result.Buffer, 0, buffer, offset, copyLen);
            }

            _length = result.Length;
            _position = result.Position;
            return result.ReadLen;
        }

        /// <summary>
        /// Асинхронный Seek на удалённом потоке.
        /// </summary>
        public async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancellationToken = default)
        {
            var result = await _indicators.Seek.RCall(new SeekInInfo(offset, origin), _client, token: cancellationToken).ConfigureAwait(false);
            if (!result.Result)
                throw new IOException("Failed to seek remote stream.");
            _position = result.Position;
            _length = result.Length;
            return result.SeekPosition;
        }

        /// <summary>
        /// Асинхронно устанавливает длину удалённого потока.
        /// </summary>
        public async Task SetLengthAsync(long value, CancellationToken cancellationToken = default)
        {
            bool result = await _indicators.SetLength.RCall(value, _client, token: cancellationToken).ConfigureAwait(false);
            if (!result)
                throw new IOException("Failed to set length on remote stream.");
            _length = value;
        }

        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("The sum of offset and count exceeds the buffer length.");

            // Отправляем только нужный фрагмент
            byte[] sendBuffer;
            int sendOffset;
            if (offset == 0 && count == buffer.Length)
            {
                sendBuffer = buffer;
                sendOffset = 0;
            }
            else
            {
                sendBuffer = new byte[count];
                Buffer.BlockCopy(buffer, offset, sendBuffer, 0, count);
                sendOffset = 0;
            }

            var result = await _indicators.Write.RCall(new WriteInInfo(sendOffset, count, sendBuffer), _client, token: cancellationToken).ConfigureAwait(false);
            if (!result.Result)
                throw new IOException("Failed to write to remote stream.");
            _length = result.Length;
            _position = result.Position;
        }

        #endregion

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    try
                    {
                        _indicators.Close.RCall(_client).GetAwaiter().GetResult();
                    }
                    catch { }
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Асинхронно закрывает удалённый поток.
        /// </summary>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (!_disposed)
            {
                _disposed = true;
                await _indicators.Close.RCall(_client, token: cancellationToken).ConfigureAwait(false);
            }
        }

        private NetStreamRemote(Client client, int id)
        {
            _indicators = new NetStreamIndicators(id);
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Открывает удалённый поток по его идентификатору.
        /// </summary>
        /// <param name="client">EMI-клиент</param>
        /// <param name="id">Идентификатор потока (полученный от <see cref="NetStreamHost"/>)</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Готовый к использованию удалённый поток</returns>
        public static async Task<NetStreamRemote> Open(Client client, int id, CancellationToken cancellationToken = default)
        {
            var stream = new NetStreamRemote(client, id);
            stream._streamInfo = await stream._indicators.GetStreamInfo.RCall(client, token: cancellationToken).ConfigureAwait(false);
            stream._position = await stream._indicators.GetStreamPosition.RCall(client, token: cancellationToken).ConfigureAwait(false);
            stream._length = await stream._indicators.GetStreamLength.RCall(client, token: cancellationToken).ConfigureAwait(false);
            return stream;
        }
    }
}
