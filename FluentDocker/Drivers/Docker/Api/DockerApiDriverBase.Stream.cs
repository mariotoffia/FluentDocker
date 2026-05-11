using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    #region NDJSON PipeReader

    /// <summary>
    /// Reads NDJSON lines from <paramref name="stream"/> using <see cref="PipeReader"/>,
    /// deserializing each line directly from UTF-8 bytes via source-generated
    /// <see cref="JsonTypeInfo{T}"/>. Eliminates the string-per-line allocation
    /// that <see cref="StreamReader"/> would incur.
    ///
    /// Skips empty, whitespace-only, and malformed JSON lines.
    /// </summary>
    protected static async IAsyncEnumerable<T> ReadNdjsonLinesAsync<T>(
        Stream stream, JsonTypeInfo<T> typeInfo,
        [EnumeratorCancellation] CancellationToken ct) where T : class
    {
      var reader = PipeReader.Create(stream);
      try
      {
        while (true)
        {
          ReadResult result;
          try
          {
            result = await reader.ReadAsync(ct).ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
            break; // ReadAsync threw — no result to AdvanceTo
          }

          var buffer = result.Buffer;
          var keepGoing = true;

          // Process all complete lines in the current buffer
          while (keepGoing && TryReadLine(ref buffer, out var lineSeq))
          {
            var item = TryDeserializeLine(lineSeq, typeInfo);
            if (item != null)
              yield return item;
            if (ct.IsCancellationRequested)
              keepGoing = false;
          }

          // AdvanceTo MUST be called after every successful ReadAsync
          reader.AdvanceTo(buffer.Start, buffer.End);

          if (!keepGoing || result.IsCompleted)
          {
            // Process any remaining data after the last newline
            if (result.IsCompleted && buffer.Length > 0)
            {
              var item = TryDeserializeLine(buffer, typeInfo);
              if (item != null)
                yield return item;
            }

            break;
          }
        }
      }
      finally
      {
        await reader.CompleteAsync().ConfigureAwait(false);
      }
    }

    /// <summary>
    /// Attempts to find and extract a line (delimited by \n) from the buffer.
    /// Advances <paramref name="buffer"/> past the consumed line + delimiter.
    /// Returns <c>false</c> if no complete line is available.
    /// </summary>
    private static bool TryReadLine(
        ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
      var position = buffer.PositionOf((byte)'\n');
      if (position == null)
      {
        line = default;
        return false;
      }

      line = buffer.Slice(0, position.Value);
      buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
      return true;
    }

    /// <summary>
    /// Deserializes a single NDJSON line from a <see cref="ReadOnlySequence{T}"/>
    /// of UTF-8 bytes. Returns <c>null</c> for empty, whitespace-only, or invalid JSON.
    /// Trims \r if present (handles \r\n line endings).
    /// </summary>
    private static T TryDeserializeLine<T>(
        ReadOnlySequence<byte> lineBytes, JsonTypeInfo<T> typeInfo) where T : class
    {
      // Trim trailing \r for \r\n line endings
      if (lineBytes.Length > 0)
      {
        var lastByte = lineBytes.Slice(lineBytes.Length - 1).FirstSpan[0];
        if (lastByte == (byte)'\r')
          lineBytes = lineBytes.Slice(0, lineBytes.Length - 1);
      }

      if (lineBytes.Length == 0)
        return null;

      // Check for whitespace-only lines (common: spaces, tabs)
      if (IsWhitespaceOnly(lineBytes))
        return null;

      try
      {
        var utf8Reader = new Utf8JsonReader(lineBytes);
        return JsonSerializer.Deserialize(ref utf8Reader, typeInfo);
      }
      catch (JsonException)
      {
        return null;
      }
    }

    private static bool IsWhitespaceOnly(ReadOnlySequence<byte> bytes)
    {
      foreach (var segment in bytes)
      {
        foreach (var b in segment.Span)
        {
          if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            return false;
        }
      }
      return true;
    }

    #endregion

    #region Stream Helpers

    /// <summary>
    /// Strips Docker multiplexed stream 8-byte header frames from raw log bytes.
    /// Frame format: [1B stream type][3B padding][4B big-endian size][payload].
    /// Operates on raw bytes to avoid UTF-8 round-trip corruption of binary headers.
    /// Uses Span-based processing for minimal allocations.
    /// </summary>
    protected static string StripDockerStreamHeaders(byte[] bytes)
    {
      if (bytes == null || bytes.Length == 0)
        return string.Empty;

      return StripDockerStreamHeaders(bytes.AsSpan());
    }

    /// <summary>
    /// Span-based overload that strips Docker multiplexed stream headers.
    /// Single-pass: computes total payload size, allocates once, decodes in place.
    /// </summary>
    protected static string StripDockerStreamHeaders(ReadOnlySpan<byte> bytes)
    {
      if (bytes.Length < 8)
        return Encoding.UTF8.GetString(bytes);

      // Check if first byte is a valid Docker stream header (0=stdin, 1=stdout, 2=stderr)
      if (bytes[0] > 2 || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0)
        return Encoding.UTF8.GetString(bytes);

      // First pass: compute total payload size to allocate once
      var totalPayload = 0;
      var offset = 0;
      while (offset + 8 <= bytes.Length)
      {
        var frameSize = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16)
                      | (bytes[offset + 6] << 8) | bytes[offset + 7];
        offset += 8;
        if (frameSize <= 0 || offset + frameSize > bytes.Length)
          break;
        totalPayload += frameSize;
        offset += frameSize;
      }

      if (totalPayload == 0)
        return Encoding.UTF8.GetString(bytes);

      // Second pass: concatenate payload bytes into a single buffer
      var payloadBuffer = totalPayload <= 1024
          ? stackalloc byte[totalPayload]
          : new byte[totalPayload];

      offset = 0;
      var writePos = 0;
      while (offset + 8 <= bytes.Length)
      {
        var frameSize = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16)
                      | (bytes[offset + 6] << 8) | bytes[offset + 7];
        offset += 8;
        if (frameSize <= 0 || offset + frameSize > bytes.Length)
          break;
        bytes.Slice(offset, frameSize).CopyTo(payloadBuffer[writePos..]);
        writePos += frameSize;
        offset += frameSize;
      }

      return Encoding.UTF8.GetString(payloadBuffer[..writePos]);
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/>,
    /// handling partial reads. Returns the total number of bytes actually read.
    /// </summary>
    protected static async Task<int> ReadExactAsync(
        Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
      var totalRead = 0;
      while (totalRead < count)
      {
        var read = await stream.ReadAsync(
            buffer.AsMemory(totalRead, count - totalRead), ct);
        if (read == 0)
          break;
        totalRead += read;
      }
      return totalRead;
    }

    #endregion
  }
}
