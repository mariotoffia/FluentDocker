#nullable disable warnings
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Connection;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    /// <summary>Docker's multiplexed (stdcopy) stream Content-Type, authoritative on API 1.42+.</summary>
    private const string MultiplexedStreamContentType = "application/vnd.docker.multiplexed-stream";

    /// <summary>Docker's raw (TTY) stream Content-Type, authoritative on API 1.42+.</summary>
    private const string RawStreamContentType = "application/vnd.docker.raw-stream";

    /// <summary>Reads the tail of a Docker log stream, stripping stdcopy headers when present.</summary>
    /// <param name="stream">The opened log response stream.</param>
    /// <param name="containerId">
    /// When supplied and the response Content-Type does not authoritatively identify the stream
    /// (pre-1.42 daemons, or an unrecognized type), a container inspect determines TTY mode
    /// instead of byte-sniffing the first frame header. A TTY container can emit binary output
    /// whose first 8 bytes coincidentally satisfy the stdcopy header shape, which byte-sniffing
    /// would misparse as multiplexed and corrupt by stripping fake "headers" throughout the log.
    /// Pass null when no single container backs the stream (e.g. a Swarm service log, which
    /// aggregates multiple tasks) — that falls back straight to the byte-sniff.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<string> ReadDockerLogTailAsync(
        Stream stream, string containerId, CancellationToken cancellationToken)
    {
      var contentType = (stream as ResponseOwningStream)?.ContentType;
      if (UseDockerLogContentType(contentType))
      {
        if (string.Equals(contentType, RawStreamContentType, StringComparison.OrdinalIgnoreCase))
          return await ReadRawLogTailAsync(stream, cancellationToken).ConfigureAwait(false);
        return await ReadMultiplexedLogTailAsync(
            stream, sniffOnInvalidHeader: false, cancellationToken).ConfigureAwait(false);
      }

      if (containerId != null)
      {
        var tty = await DetectTtyAsync(containerId, cancellationToken).ConfigureAwait(false);
        if (tty == true)
          return await ReadRawLogTailAsync(stream, cancellationToken).ConfigureAwait(false);
        if (tty == false)
          return await ReadMultiplexedLogTailAsync(
              stream, sniffOnInvalidHeader: false, cancellationToken).ConfigureAwait(false);
      }

      // containerId is null, or inspect could not determine TTY mode: fall back to the
      // byte-sniff, the closest guess available.
      return await ReadMultiplexedLogTailAsync(
          stream, sniffOnInvalidHeader: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inspects the container to determine whether it was started with a TTY. A TTY stream is
    /// raw text (no multiplex headers). On inspect failure this returns null, so the caller
    /// falls back to byte-sniffing the stream to decide multiplexing. The
    /// Content-Type: application/vnd.docker.multiplexed-stream response header (API >= 1.42) is
    /// the authoritative future seam; this is the pre-1.42 / unrecognized-type fallback shared by
    /// the log-tail and log-stream readers.
    /// </summary>
    protected async Task<bool?> DetectTtyAsync(string containerId, CancellationToken ct)
    {
      try
      {
        var result = await GetJsonElementAsync(
            $"/containers/{Uri.EscapeDataString(containerId)}/json", ct).ConfigureAwait(false);
        if (result.Success && result.Data.ValueKind == JsonValueKind.Object)
        {
          var config = result.Data.Prop("Config");
          if (config?.ValueKind == JsonValueKind.Object)
            return config.Value.GetBoolOrDefault("Tty");
        }
      }
      catch (Exception ex)
      {
        Logger.LogDebug(ex, "Could not determine container TTY mode; defaulting to demux");
      }
      return null;
    }

    private bool UseDockerLogContentType(string contentType)
    {
      return (string.Equals(contentType, MultiplexedStreamContentType, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(contentType, RawStreamContentType, StringComparison.OrdinalIgnoreCase)) &&
          Version.TryParse(Connection.ApiVersion, out var version) &&
          version.CompareTo(new Version(1, 42)) >= 0;
    }

    private static async Task<string> ReadRawLogTailAsync(
        Stream stream, CancellationToken cancellationToken)
    {
      var tail = new TailBytes(CliOutputTruncation.DefaultTailChars);
      await CopyTailAsync(stream, tail, cancellationToken).ConfigureAwait(false);
      return tail.ToText();
    }

    private static async Task<string> ReadMultiplexedLogTailAsync(
        Stream stream, bool sniffOnInvalidHeader, CancellationToken cancellationToken)
    {
      var tail = new TailBytes(CliOutputTruncation.DefaultTailChars);
      var header = new byte[8];
      var read = await ReadExactAsync(stream, header, 8, cancellationToken).ConfigureAwait(false);
      if (read == 0)
        return string.Empty;
      if (read < 8 || !IsValidStdCopyHeader(header))
      {
        if (!sniffOnInvalidHeader)
          throw new DriverException(
              read < 8
                  ? $"Docker stream truncated: partial {read}-byte frame header"
                  : "Docker stream has an invalid multiplexed frame header",
              Model.Drivers.ErrorCodes.Api.ServerError);
        tail.Append(header.AsSpan(0, read));
        await CopyTailAsync(stream, tail, cancellationToken).ConfigureAwait(false);
        return tail.ToText();
      }

      while (read == 8)
      {
        if (!IsValidStdCopyHeader(header))
          throw new DriverException(
              "Docker stream has an invalid multiplexed frame header",
              Model.Drivers.ErrorCodes.Api.ServerError);
        var frameSize = (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
        if (frameSize < 0 || frameSize > MaxFrameSizeBytes)
          throw new DriverException(
              $"Docker stream frame size {frameSize} is invalid or exceeds the {MaxFrameSizeBytes} byte limit",
              Model.Drivers.ErrorCodes.Api.ServerError);
        await CopyFrameTailAsync(stream, frameSize, tail, cancellationToken).ConfigureAwait(false);
        read = await ReadExactAsync(stream, header, 8, cancellationToken).ConfigureAwait(false);
      }
      if (read != 0)
        throw new DriverException($"Docker stream truncated: partial {read}-byte frame header",
            Model.Drivers.ErrorCodes.Api.ServerError);
      return tail.ToText();
    }

    private static bool IsValidStdCopyHeader(byte[] header) =>
        header[0] <= 3 && header[1] == 0 && header[2] == 0 && header[3] == 0;

    private static async Task CopyTailAsync(Stream stream, TailBytes tail, CancellationToken ct)
    {
      var buffer = new byte[81920];
      int read;
      while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        tail.Append(buffer.AsSpan(0, read));
    }

    private static async Task CopyFrameTailAsync(
        Stream stream, int count, TailBytes tail, CancellationToken ct)
    {
      var buffer = new byte[Math.Min(81920, Math.Max(1, count))];
      var remaining = count;
      while (remaining > 0)
      {
        var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct)
            .ConfigureAwait(false);
        if (read == 0)
          throw new DriverException(
              $"Docker stream truncated: expected {count} payload bytes, read {count - remaining}",
              Model.Drivers.ErrorCodes.Api.ServerError);
        tail.Append(buffer.AsSpan(0, read));
        remaining -= read;
      }
    }

    private sealed class TailBytes(int maxBytes)
    {
      private readonly byte[] _buffer = new byte[maxBytes];
      private int _start;
      private int _count;
      private bool _truncated;

      /// <summary>Appends bytes while retaining only the configured tail window.</summary>
      public void Append(ReadOnlySpan<byte> bytes)
      {
        if (bytes.Length > _buffer.Length)
        {
          bytes[^_buffer.Length..].CopyTo(_buffer);
          _start = 0;
          _count = _buffer.Length;
          _truncated = true;
          return;
        }

        foreach (var b in bytes)
        {
          if (_count < _buffer.Length)
          {
            _buffer[(_start + _count) % _buffer.Length] = b;
            _count++;
          }
          else
          {
            _buffer[_start] = b;
            _start = (_start + 1) % _buffer.Length;
            _truncated = true;
          }
        }
      }

      /// <summary>Decodes the retained tail bytes, prefixing a truncation marker when needed.</summary>
      public string ToText()
      {
        var bytes = new byte[_count];
        for (var i = 0; i < _count; i++)
          bytes[i] = _buffer[(_start + i) % _buffer.Length];
        var text = Encoding.UTF8.GetString(bytes);
        // The ring buffer evicts whole bytes, not whole characters, so a truncated tail can
        // begin mid-way through a multi-byte UTF-8 sequence, which decodes as U+FFFD. Strip
        // those leading replacement chars so the tail starts at the first complete character
        // (only when truncated — a full tail's U+FFFD is genuine daemon output).
        if (_truncated)
          text = text.TrimStart('\uFFFD');
        return _truncated
            ? CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars) + Environment.NewLine + text
            : text;
      }
    }
  }
}
