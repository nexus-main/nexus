using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal class DataSourceDoubleStream : Stream
    {
        #region Fields

        private readonly CancellationTokenSource _cts = new();
        private long _position;
        private readonly long _length;
        private readonly PipeReader _reader;
        private readonly Stream _stream;

        #endregion

        #region Constructors

        public DataSourceDoubleStream(long length, PipeReader reader)
        {
            _length = length;
            _reader = reader;
            _stream = reader.AsStream();
        }

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get
            {
                return _position;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        #endregion

        #region Methods

        public void Cancel()
        {
            _reader.CancelPendingRead();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override int Read(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        {
            throw new NotImplementedException();
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            throw new NotImplementedException();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var readCount = await _stream.ReadAsync(buffer, _cts.Token);
            _position += readCount;

            return readCount;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            _stream.Dispose();
        }

        #endregion
    }
}
