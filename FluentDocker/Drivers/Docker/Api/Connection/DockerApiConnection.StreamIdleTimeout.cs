using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ResponseOwningStream = FluentDocker.Drivers.Connection.ResponseOwningStream;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  public sealed partial class DockerApiConnection
  {
    private Stream CreateResponseStream(Stream stream, HttpResponseMessage response)
    {
      var body = _config.StreamIdleTimeout.HasValue
          ? new DockerApiReadIdleTimeoutStream(stream, _config.StreamIdleTimeout.Value)
          : stream;
      return new ResponseOwningStream(body, response);
    }

    private sealed class DockerApiReadIdleTimeoutStream(Stream inner, TimeSpan timeout) : Stream
    {
      private int _disposed;
      private int _timedOut;

      public override bool CanRead => !IsDisposed && inner.CanRead;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

      public override int Read(byte[] buffer, int offset, int count) =>
          ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask()
              .GetAwaiter().GetResult();

      public override async ValueTask<int> ReadAsync(
          Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (Volatile.Read(ref _timedOut) != 0)
          throw CreateTimeoutException();
        if (buffer.Length == 0)
          return 0;

        var readTask = inner.ReadAsync(buffer, cancellationToken).AsTask();
        using var delayCts = new CancellationTokenSource();
        var delayTask = Task.Delay(timeout, delayCts.Token);
        var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
        if (completed == readTask)
        {
          delayCts.Cancel();
          return await readTask.ConfigureAwait(false);
        }

        Volatile.Write(ref _timedOut, 1);
        throw CreateTimeoutException();
      }

      public override Task<int> ReadAsync(
          byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
          ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      public override void Flush() { }

      protected override void Dispose(bool disposing)
      {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
          return;
        if (disposing)
          inner.Dispose();
        base.Dispose(disposing);
      }

      public override async ValueTask DisposeAsync()
      {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
          return;
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
      }

      private TimeoutException CreateTimeoutException() =>
          new($"Docker API stream read idle timeout elapsed after {timeout} without receiving bytes.");
    }
  }
}
