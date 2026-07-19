#nullable disable warnings
using System;
using System.Buffers;
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

        // DAPI-1: read into a wrapper-owned buffer and copy out only on success. If the idle timeout
        // fires, inner.ReadAsync is abandoned but still owns whatever buffer we handed it; the caller
        // recycles its buffer to ArrayPool the moment we throw, so the abandoned read must never hold it.
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        Task<int> readTask = null;
        try
        {
          readTask = inner.ReadAsync(rented.AsMemory(0, buffer.Length), cancellationToken).AsTask();
          var read = await readTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
          rented.AsMemory(0, read).CopyTo(buffer);
          return read;
        }
        catch (TimeoutException)
        {
          Volatile.Write(ref _timedOut, 1);
          throw CreateTimeoutException();
        }
        finally
        {
          // DAPI-8: the pool gets `rented` back exactly once on every path (success,
          // cancellation, IOException, timeout). When WaitAsync abandoned a still-pending
          // inner read (idle timeout or caller cancellation), returning immediately could
          // recycle a segment the abandoned read later writes into (DAPI-1), so ownership
          // passes to a continuation that returns the buffer once that read settles.
          if (readTask is { IsCompleted: false })
          {
            _ = readTask.ContinueWith(
                static (task, state) =>
                {
                  _ = task.Exception; // observe a late fault so it is never unobserved
                  ArrayPool<byte>.Shared.Return((byte[])state);
                },
                rented, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
          }
          else
          {
            ArrayPool<byte>.Shared.Return(rented);
          }
        }
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
