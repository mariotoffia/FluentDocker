using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using ResponseOwningStream = FluentDocker.Drivers.Connection.ResponseOwningStream;

namespace FluentDocker.Drivers.Models.Connection
{
  public sealed partial class ModelApiConnection
  {
    /// <inheritdoc />
    public async Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default)
    {
      // Streaming is exempt from the whole-request timeout (SSE can run for a long time), but the
      // first-byte/header wait still has its own budget so a wedged runner cannot hang.
      var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
      HttpResponseMessage response = null;
      var transferred = false;
      using var headerCts = _streamFirstByteTimeout is null
          ? null
          : CancellationTokenSource.CreateLinkedTokenSource(ct);
      headerCts?.CancelAfter(_streamFirstByteTimeout.GetValueOrDefault());
      var headerToken = headerCts?.Token ?? ct;

      try
      {
        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && _streamFirstByteTimeout is not null && !IsTransportFailure(ex))
      {
        request.Dispose();
        throw new ModelRunnerException(
            "Streaming response headers timed out: no first byte received within the configured first-byte timeout.",
            ErrorCodes.ModelInference.Timeout, ex);
      }
      catch (Exception ex) when (!ct.IsCancellationRequested && IsTransportFailure(ex))
      {
        // A connection-refused / DNS / socket failure opening the stream is "unreachable".
        // (An HTTP error STATUS is delivered as a response below, not thrown here.)
        request.Dispose();
        throw EndpointUnreachable(ex);
      }
      catch
      {
        request.Dispose();
        throw;
      }
      try
      {
        if (!response.IsSuccessStatusCode)
        {
          // Surface the status code AND a bounded error body so the inference driver can
          // map it to a typed ModelRunnerException (404 -> ModelNotLoaded, 401 ->
          // Unauthorized), mirroring the non-streaming path. EnsureSuccessStatusCode would
          // discard the body. Dispose the failed response before throwing so it does not
          // leak — ownership has not yet been transferred to ResponseOwningStream.
          var status = response.StatusCode;
          string body;
          try
          {
            body = await ReadBoundedErrorBodyAsync(response, ct).ConfigureAwait(false);
          }
          finally
          {
            response.Dispose();
          }

          throw new HttpRequestException(
              string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)status}" : body, null, status);
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        transferred = true;
        Stream owned = new ResponseOwningStream(stream, response);
        // Enforce the documented streaming budgets on the RETURNED stream, not only on the
        // header phase: IModelApiConnection promises reads abort with
        // ModelRunnerException(Timeout) once StreamFirstByteTimeout/StreamReadIdleTimeout
        // elapse, for every consumer — not just drivers that re-implement enforcement.
        if (_streamFirstByteTimeout is not null || _streamReadIdleTimeout is not null)
          owned = new IdleTimeoutReadStream(owned, _streamFirstByteTimeout, _streamReadIdleTimeout);
        return new RequestOwningStream(owned, request);
      }
      catch
      {
        if (!transferred)
        {
          response?.Dispose();
          request.Dispose();
        }
        throw;
      }
    }

    /// <summary>
    /// Enforces the connection's streaming read budgets on the stream returned by
    /// <see cref="PostStreamAsync"/>: the first body read is bounded by
    /// <see cref="IModelApiConnection.StreamFirstByteTimeout"/> (falling back to the idle
    /// timeout when unset) and every subsequent read by
    /// <see cref="IModelApiConnection.StreamReadIdleTimeout"/>, aborting with
    /// <see cref="ModelRunnerException"/> (<see cref="ErrorCodes.ModelInference.Timeout"/>) as the
    /// interface documents. Caller cancellation propagates unchanged. Synchronous reads are not
    /// supported (matching <see cref="TimeoutReadStream"/> — model API response bodies are
    /// async-only).
    /// </summary>
    private sealed class IdleTimeoutReadStream(Stream inner, TimeSpan? firstByteTimeout, TimeSpan? idleTimeout) : Stream
    {
      private bool _readAny;

      public override bool CanRead => inner.CanRead;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        var budget = _readAny ? idleTimeout : firstByteTimeout ?? idleTimeout;
        if (budget is null)
        {
          var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
          _readAny = true;
          return read;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(budget.GetValueOrDefault());
        try
        {
          var read = await inner.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
          _readAny = true;
          return read;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
          throw new ModelRunnerException(
              _readAny
                  ? "Streaming read timed out: no data received within the configured idle timeout."
                  : "Streaming response timed out: no first byte received within the configured first-byte timeout.",
              ErrorCodes.ModelInference.Timeout, ex);
        }
      }

      public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
          ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

      public override int Read(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException("Synchronous model API response body reads are not supported; use ReadAsync.");
      public override void Flush()
      {
      }

      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

      protected override void Dispose(bool disposing)
      {
        if (disposing)
          inner.Dispose();
        base.Dispose(disposing);
      }

      public override async ValueTask DisposeAsync()
      {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
      }
    }

    private sealed class RequestOwningStream(Stream inner, HttpRequestMessage request) : Stream
    {
      public override bool CanRead => inner.CanRead;
      public override bool CanSeek => inner.CanSeek;
      public override bool CanWrite => false;
      public override long Length => inner.Length;
      public override long Position { get => inner.Position; set => inner.Position = value; }
      public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
          await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
          await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
      public override void Flush() => inner.Flush();
      public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

      protected override void Dispose(bool disposing)
      {
        if (disposing)
        {
          try
          {
            inner.Dispose();
          }
          finally
          {
            request.Dispose();
          }
        }
        base.Dispose(disposing);
      }

      public override async ValueTask DisposeAsync()
      {
        try
        {
          await inner.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
          request.Dispose();
        }
        await base.DisposeAsync().ConfigureAwait(false);
      }
    }

    /// <summary>
    /// Hard cap on how many bytes of a non-success response body are read into memory
    /// before building an exception message. A hostile or misbehaving server could send an
    /// arbitrarily large error body; bounding the READ (not just the final string) keeps
    /// error handling allocation-safe.
    /// </summary>
    private const int MaxErrorBodyBytes = 64 * 1024;

    /// <summary>
    /// Maximum number of characters from the body that are kept in the exception message.
    /// Anything past this is truncated and replaced with <see cref="ErrorBodyTruncationMarker"/>.
    /// </summary>
    private const int MaxErrorBodyChars = 512;

    /// <summary>Appended to a truncated error body so it is visibly incomplete.</summary>
    private const string ErrorBodyTruncationMarker = "…";

    /// <summary>
    /// Reads a non-success response body, bounded both in bytes read (<see cref="MaxErrorBodyBytes"/>)
    /// and in characters retained (<see cref="MaxErrorBodyChars"/>), for use in an exception
    /// message. Caller cancellation propagates; any other read failure is swallowed (it must not
    /// mask the underlying HTTP failure).
    /// </summary>
    private async Task<string> ReadBoundedErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
      CancellationTokenSource idleCts = null;
      try
      {
        idleCts = _streamReadIdleTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        var body = await ReadBoundedBodyTextAsync(
            response, idleCts, _streamReadIdleTimeout, idleCts?.Token ?? ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
          return null;

        if (body.Length <= MaxErrorBodyChars)
          return body;

        // Keep the marker WITHIN the cap so the final message length never exceeds it.
        var keep = MaxErrorBodyChars - ErrorBodyTruncationMarker.Length;
        return body[..keep] + ErrorBodyTruncationMarker;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && idleCts?.IsCancellationRequested == true)
      {
        throw new ModelRunnerException(
            "Streaming error response body timed out: no data received within the configured idle timeout.",
            ErrorCodes.ModelInference.Timeout, ex);
      }
      catch (Exception)
      {
        return null;
      }
      finally
      {
        idleCts?.Dispose();
      }
    }

    /// <summary>
    /// Reads at most <see cref="MaxErrorBodyBytes"/> bytes of the response body and decodes
    /// them as UTF-8, so a pathological error body cannot force unbounded buffering.
    /// </summary>
    private static async Task<string> ReadBoundedBodyTextAsync(
        HttpResponseMessage response, CancellationTokenSource idleCts, TimeSpan? idleTimeout, CancellationToken ct)
    {
      ArmIdleTimer(idleCts, idleTimeout);
      await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      var buffer = new byte[MaxErrorBodyBytes];
      var total = 0;
      while (total < buffer.Length)
      {
        ArmIdleTimer(idleCts, idleTimeout);
        var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
        if (read == 0)
          break;
        total += read;
      }

      return total == 0 ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static void ArmIdleTimer(CancellationTokenSource cts, TimeSpan? timeout)
    {
      if (cts is not null && timeout is not null)
        cts.CancelAfter(timeout.GetValueOrDefault());
    }
  }
}
