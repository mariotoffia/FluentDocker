using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    /// <summary>Reads the tail of a Docker log stream, stripping stdcopy headers when present.</summary>
    protected static async Task<string> ReadDockerLogTailAsync(
        Stream stream, CancellationToken cancellationToken)
    {
      var tail = new TailBytes(CliOutputTruncation.DefaultTailChars);
      var header = new byte[8];
      var read = await ReadExactAsync(stream, header, 8, cancellationToken).ConfigureAwait(false);
      if (read == 0)
        return string.Empty;
      if (read < 8 || header[0] > 2 || header[1] != 0 || header[2] != 0 || header[3] != 0)
      {
        tail.Append(header.AsSpan(0, read));
        await CopyTailAsync(stream, tail, cancellationToken).ConfigureAwait(false);
        return tail.ToText();
      }

      while (read == 8)
      {
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

      /// <inheritdoc />
      public void Append(ReadOnlySpan<byte> bytes)
      {
        if (bytes.Length >= _buffer.Length)
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

      /// <inheritdoc />
      public string ToText()
      {
        var bytes = new byte[_count];
        for (var i = 0; i < _count; i++)
          bytes[i] = _buffer[(_start + i) % _buffer.Length];
        var text = Encoding.UTF8.GetString(bytes);
        return _truncated
            ? CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars) + Environment.NewLine + text
            : text;
      }
    }
  }
}
