using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EMI;
using EMI.NGC;

namespace EMI.NetStream
{
    using Structures;

    /// <summary>
    /// Хост-обёртка над локальным <see cref="Stream"/>, регистрирующая RPC-методы
    /// для удалённого доступа через <see cref="NetStreamRemote"/>.
    /// </summary>
    public class NetStreamHost : IDisposable
    {
        private static int _nextId;

        private readonly NetStreamIndicators _indicators;
        private readonly List<IRPCRemoveHandle> _handles = new List<IRPCRemoveHandle>();
        private readonly Stream _stream;
        private readonly object _sync = new object();
        private bool _disposed;

        /// <summary>
        /// Уникальный идентификатор потока (передаётся удалённой стороне для открытия <see cref="NetStreamRemote"/>).
        /// </summary>
        public int ID { get; }

        /// <summary>
        /// Открыт ли хост (зарегистрированы ли RPC-обработчики).
        /// </summary>
        public bool IsOpen
        {
            get { lock (_sync) { return _handles.Count > 0; } }
        }

        private NetStreamHost(Client client, Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            ID = Interlocked.Increment(ref _nextId);
            _indicators = new NetStreamIndicators(ID);

            _handles.Add(client.LocalRPC.RegisterMethod(GetStreamInfo, _indicators.GetStreamInfo));
            _handles.Add(client.LocalRPC.RegisterMethod(GetStreamLength, _indicators.GetStreamLength));
            _handles.Add(client.LocalRPC.RegisterMethod(GetStreamPosition, _indicators.GetStreamPosition));
            _handles.Add(client.LocalRPC.RegisterMethod(SetStreamPosition, _indicators.SetStreamPosition));
            _handles.Add(client.LocalRPC.RegisterMethod(Flush, _indicators.Flush));
            _handles.Add(client.LocalRPC.RegisterMethod(Read, _indicators.Read));
            _handles.Add(client.LocalRPC.RegisterMethod(Seek, _indicators.Seek));
            _handles.Add(client.LocalRPC.RegisterMethod(SetLength, _indicators.SetLength));
            _handles.Add(client.LocalRPC.RegisterMethod(Write, _indicators.Write));
            _handles.Add(client.LocalRPC.RegisterMethod(_Close, _indicators.Close));
        }

        #region RPC handlers

        private NetStreamInfo GetStreamInfo()
        {
            return new NetStreamInfo(_stream);
        }

        private long GetStreamLength()
        {
            try { return _stream.Length; }
            catch { return -1; }
        }

        private long GetStreamPosition()
        {
            try { return _stream.Position; }
            catch { return -1; }
        }

        private bool SetStreamPosition(long position)
        {
            try
            {
                _stream.Position = position;
                return true;
            }
            catch { return false; }
        }

        private FlushInfo Flush()
        {
            try
            {
                _stream.Flush();
                return new FlushInfo(true, GetStreamLength(), GetStreamPosition());
            }
            catch { return new FlushInfo(false, -1, -1); }
        }

        private ReadInfo Read(ReadInInfo readIn)
        {
            try
            {
                int count = readIn.Count;
                byte[] buffer = new byte[count];
                int len = _stream.Read(buffer, 0, count);

                // Если прочитано меньше чем запрошено — обрезаем массив
                if (len < count)
                {
                    byte[] trimmed = new byte[len];
                    Buffer.BlockCopy(buffer, 0, trimmed, 0, len);
                    buffer = trimmed;
                }

                return new ReadInfo(true, len, buffer, _stream.Length, _stream.Position);
            }
            catch
            {
                return new ReadInfo(false, -1, null, -1, -1);
            }
        }

        private SeekInfo Seek(SeekInInfo info)
        {
            try
            {
                long result = _stream.Seek(info.Offset, info.Origin);
                return new SeekInfo(true, _stream.Length, _stream.Position, result);
            }
            catch { return new SeekInfo(false, -1, -1, -1); }
        }

        private bool SetLength(long length)
        {
            try
            {
                _stream.SetLength(length);
                return true;
            }
            catch { return false; }
        }

        private WriteInfo Write(WriteInInfo write)
        {
            try
            {
                _stream.Write(write.Buffer, write.Offset, write.Count);
                return new WriteInfo(true, _stream.Position, _stream.Length);
            }
            catch { return new WriteInfo(false, -1, -1); }
        }

        #endregion

        /// <summary>
        /// Создаёт новый хост для потока и регистрирует RPC-обработчики.
        /// </summary>
        /// <param name="client">EMI-клиент, на котором регистрируются RPC</param>
        /// <param name="stream">Локальный поток</param>
        /// <returns>Объект хоста (содержит <see cref="ID"/> для передачи удалённой стороне)</returns>
        public static NetStreamHost Create(Client client, Stream stream)
        {
            return new NetStreamHost(client, stream);
        }

        private void _Close()
        {
            lock (_sync)
            {
                foreach (var handle in _handles)
                {
                    try { handle.Remove(); } catch { }
                }
                _handles.Clear();
            }
        }

        /// <summary>
        /// Закрывает хост и удаляет все зарегистрированные RPC-обработчики.
        /// </summary>
        public void Close()
        {
            lock (_sync)
            {
                if (_handles.Count == 0)
                    throw new InvalidOperationException("NetStreamHost is already closed.");
            }
            _Close();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _Close();
        }
    }
}
