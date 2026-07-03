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
        Stream stream, bool tty, [EnumeratorCancellation] CancellationToken ct)
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
          throw new DriverException(
              $"Docker log stream truncated: partial {bytesRead}-byte frame header",
              ErrorCodes.Api.ServerError);
        }

        if (header[0] > 2 || header[1] != 0 || header[2] != 0 || header[3] != 0)
        {
          throw new DriverException(
              "Docker log stream has an invalid multiplexed frame header",
              ErrorCodes.Api.ServerError);
        }

        var state = StateFor(header[0], stdin, stdout, stderr);
        var frameSize = (header[4] << 24) | (header[5] << 16) |
            (header[6] << 8) | header[7];

        if (frameSize < 0 || frameSize > MaxFrameSizeBytes)
          throw new DriverException(
              $"Docker log stream frame size {frameSize} is invalid or exceeds the {MaxFrameSizeBytes} byte limit",
              ErrorCodes.Api.ServerError);
        if (frameSize == 0)
          continue;

        var payload = new byte[frameSize];
        var payloadRead = await ReadExactAsync(stream, payload, frameSize, ct).ConfigureAwait(false);
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
    /// Reads a raw (TTY) log stream as plain UTF-8 text, yielding each non-empty line as
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

        if (line == null)
          break;
        if (line.Length == 0)
          continue;
        yield return new LogEntry { Source = LogStreamSource.Stdout, Line = line };
        ct.ThrowIfCancellationRequested();
      }
    }

    private sealed class Utf8LineState(LogStreamSource source)
    {
      private readonly StringBuilder _pendingText = new();
      private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
      private bool _midCharacter;

      public IEnumerable<LogEntry> Append(byte[] payload, int count)
      {
        var wasMidCharacter = _midCharacter;
        var chars = new char[Encoding.UTF8.GetMaxCharCount(count)];
        var written = _decoder.GetChars(payload, 0, count, chars, 0, flush: false);
        _pendingText.Append(chars, 0, written);

        // GetCharCount with flush:true simulates a flush without mutating decoder state;
        // a nonzero count means the frame ended mid-character, so hold the entry open
        // until the next frame completes it.
        _midCharacter = _decoder.GetCharCount([], 0, 0, flush: true) > 0;

        // A held partial that decodes to U+FFFD was abandoned by the stream — stop
        // holding the entry open (keeps broken input at frame-granular emission).
        var abandoned = wasMidCharacter && written > 0 && chars[0] == '\uFFFD';
        if (!_midCharacter || abandoned || _pendingText.Length >= MaxFrameSizeBytes)
          return EmitPending();
        return Array.Empty<LogEntry>();
      }

      public IEnumerable<LogEntry> Flush()
      {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(0)];
        var written = _decoder.GetChars([], 0, 0, chars, 0, flush: true);
        _pendingText.Append(chars, 0, written);
        _midCharacter = false;
        return EmitPending();
      }

      private IEnumerable<LogEntry> EmitPending()
      {
        var entries = new List<LogEntry>();
        if (_pendingText.Length == 0)
          return entries;

        var text = _pendingText.ToString().TrimEnd('\n', '\r');
        _pendingText.Clear();
        foreach (var raw in text.Split('\n'))
        {
          var line = raw.EndsWith('\r') ? raw[..^1] : raw;
          if (line.Length > 0)
            entries.Add(new LogEntry { Source = source, Line = line });
        }
        return entries;
      }
    }

    #endregion
  }
}
