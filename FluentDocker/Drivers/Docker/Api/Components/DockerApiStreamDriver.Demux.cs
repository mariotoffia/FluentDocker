using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiStreamDriver
  {
    #region Multiplexed Stream Reader

    /// <summary>
    /// Reads Docker multiplexed stream format, tagging each line with its source stream.
    /// Header: [stream_type:1][0:3][size:4 big-endian] followed by payload.
    /// stream_type: 0=stdin, 1=stdout, 2=stderr.
    /// When <paramref name="tty"/> is true the stream is raw text (no headers), so
    /// demultiplexing is bypassed and every line is tagged as stdout.
    /// </summary>
    private static async IAsyncEnumerable<LogEntry> ReadMultiplexedStreamAsync(
        Stream stream, bool tty, bool sniffOnInvalidHeader,
        [EnumeratorCancellation] CancellationToken ct)
    {
      if (tty)
      {
        await foreach (var entry in ReadRawTextStreamAsync(stream, ct).ConfigureAwait(false))
          yield return entry;
        yield break;
      }

      var header = new byte[8];
      var stdin = new Utf8LineState(LogStreamSource.Stdin);
      var stdout = new Utf8LineState(LogStreamSource.Stdout);
      var stderr = new Utf8LineState(LogStreamSource.Stderr);
      var parsedFrame = false;

      while (true)
      {
        ct.ThrowIfCancellationRequested();
        int bytesRead;
        try
        {
          bytesRead = await ReadExactAsync(stream, header, 8, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          throw new DriverException(
              $"Docker log stream read failed: {ex.Message}", ErrorCodes.Api.ServerError, ex);
        }

        if (bytesRead == 0)
          break;
        if (bytesRead < 8)
        {
          if (sniffOnInvalidHeader && !parsedFrame)
          {
            await foreach (var entry in ReadRawTextStreamAsync(
                new PrefixReadStream(header, bytesRead, stream), ct).ConfigureAwait(false))
              yield return entry;
            yield break;
          }
          throw new DriverException(
              $"Docker log stream truncated: partial {bytesRead}-byte frame header",
              ErrorCodes.Api.ServerError);
        }

        if (header[0] > 2 || header[1] != 0 || header[2] != 0 || header[3] != 0)
        {
          if (sniffOnInvalidHeader && !parsedFrame)
          {
            await foreach (var entry in ReadRawTextStreamAsync(
                new PrefixReadStream(header, bytesRead, stream), ct).ConfigureAwait(false))
              yield return entry;
            yield break;
          }
          throw new DriverException(
              "Docker log stream has an invalid multiplexed frame header",
              ErrorCodes.Api.ServerError);
        }

        var state = StateFor(header[0], stdin, stdout, stderr);
        var frameSize = (header[4] << 24) | (header[5] << 16) |
            (header[6] << 8) | header[7];
        parsedFrame = true;

        if (frameSize < 0 || frameSize > MaxFrameSizeBytes)
          throw new DriverException(
              $"Docker log stream frame size {frameSize} is invalid or exceeds the {MaxFrameSizeBytes} byte limit",
              ErrorCodes.Api.ServerError);
        if (frameSize == 0)
          continue;

        var payload = new byte[frameSize];
        int payloadRead;
        try
        {
          payloadRead = await ReadExactAsync(stream, payload, frameSize, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          throw new DriverException(
              $"Docker log stream read failed: {ex.Message}", ErrorCodes.Api.ServerError, ex);
        }
        if (payloadRead < frameSize)
          throw new DriverException(
              $"Docker log stream truncated: expected {frameSize} payload bytes, read {payloadRead}",
              ErrorCodes.Api.ServerError);

        foreach (var entry in state.Append(payload, payloadRead))
        {
          yield return entry;
          ct.ThrowIfCancellationRequested();
        }
      }

      foreach (var entry in Flush(stdin, stdout, stderr))
      {
        yield return entry;
        ct.ThrowIfCancellationRequested();
      }
    }

    private static Utf8LineState StateFor(
        byte streamType, Utf8LineState stdin, Utf8LineState stdout, Utf8LineState stderr) =>
        streamType switch
        {
          0 => stdin,
          2 => stderr,
          _ => stdout,
        };

    private static IEnumerable<LogEntry> Flush(params Utf8LineState[] states)
    {
      foreach (var state in states)
      {
        foreach (var entry in state.Flush())
          yield return entry;
      }
    }

    /// <summary>
    /// Reads a raw (TTY) log stream as plain UTF-8 text, yielding each line as
    /// stdout. Raw streams carry no source byte, so stderr cannot be distinguished.
    /// </summary>
    private static async IAsyncEnumerable<LogEntry> ReadRawTextStreamAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
      using var reader = new StreamReader(stream, Encoding.UTF8,
          detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
      while (true)
      {
        ct.ThrowIfCancellationRequested();
        string line;
        try
        {
          line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          throw new DriverException(
              $"Docker log stream read failed: {ex.Message}", ErrorCodes.Api.ServerError, ex);
        }

        if (line == null)
          break;
        yield return new LogEntry { Source = LogStreamSource.Stdout, Line = line };
        ct.ThrowIfCancellationRequested();
      }
    }

    private sealed class Utf8LineState(LogStreamSource source)
    {
      private readonly StringBuilder _pendingText = new();
      private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

      public IEnumerable<LogEntry> Append(byte[] payload, int count)
      {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(count)];
        var written = _decoder.GetChars(payload, 0, count, chars, 0, flush: false);
        var entries = new List<LogEntry>();

        // Single pass over the freshly decoded span: emit on each '\n', keep only the
        // trailing partial line in _pendingText. Each char is appended and scanned once,
        // so a newline-dense frame is O(n) (the old per-line StringBuilder.Remove was O(n^2)).
        var start = 0;
        for (var i = 0; i < written; i++)
        {
          if (chars[i] != '\n')
            continue;
          _pendingText.Append(chars, start, i - start);
          entries.Add(EmitLine());
          start = i + 1;
        }

        _pendingText.Append(chars, start, written - start);

        // ponytail: cap an unterminated line at one stdcopy-frame max so a newline-free
        // stream can't grow _pendingText without bound; switch to chunk callbacks if exact
        // giant-line fidelity matters.
        if (_pendingText.Length >= MaxFrameSizeBytes)
          entries.Add(EmitLine());

        return entries;
      }

      public IEnumerable<LogEntry> Flush()
      {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(0)];
        var written = _decoder.GetChars([], 0, 0, chars, 0, flush: true);
        _pendingText.Append(chars, 0, written);
        return _pendingText.Length == 0 ? Array.Empty<LogEntry>() : new[] { EmitLine() };
      }

      private LogEntry EmitLine()
      {
        var line = _pendingText.ToString();
        _pendingText.Clear();
        if (line.EndsWith('\r'))
          line = line[..^1];
        return new LogEntry { Source = source, Line = line };
      }
    }

    private sealed class PrefixReadStream(byte[] prefix, int prefixLength, Stream inner) : Stream
    {
      private int _offset;

      /// <inheritdoc />
      public override bool CanRead => true;
      /// <inheritdoc />
      public override bool CanSeek => false;
      /// <inheritdoc />
      public override bool CanWrite => false;
      /// <inheritdoc />
      public override long Length => throw new NotSupportedException();
      /// <inheritdoc />
      public override long Position
      {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      /// <inheritdoc />
      public override async ValueTask<int> ReadAsync(
          Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        if (_offset < prefixLength)
        {
          var count = Math.Min(prefixLength - _offset, buffer.Length);
          prefix.AsMemory(_offset, count).CopyTo(buffer);
          _offset += count;
          return count;
        }
        return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
      }

      /// <inheritdoc />
      public override int Read(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException("synchronous Read is not supported; use ReadAsync");

      /// <inheritdoc />
      public override void Flush() { }
      /// <inheritdoc />
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      /// <inheritdoc />
      public override void SetLength(long value) => throw new NotSupportedException();
      /// <inheritdoc />
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
  }
}
