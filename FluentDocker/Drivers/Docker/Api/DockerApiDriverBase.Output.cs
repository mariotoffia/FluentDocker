using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;

namespace FluentDocker.Drivers.Docker.Api
{
  public abstract partial class DockerApiDriverBase
  {
    /// <summary>Extracts the HTTP status code from a transport exception, or 0 if none applies.</summary>
    protected static int HttpStatusCodeOrZero(Exception ex) =>
        ex switch
        {
          HttpRequestException { StatusCode: not null } httpEx => (int)httpEx.StatusCode.Value,
          DockerApiTtfbTimeoutException => 408,
          _ => 0
        };

    /// <summary>Creates an empty <see cref="TailText"/> sized to the default CLI output tail.</summary>
    protected static TailText CreateOutputTail() =>
        new(CliOutputTruncation.DefaultTailChars);

    /// <summary>Reads a stream to completion and returns its tail, truncated to the default size.</summary>
    protected static async Task<string> ReadTextTailAsync(
        Stream stream, CancellationToken cancellationToken)
    {
      var tail = CreateOutputTail();
      using var reader = new StreamReader(stream, Encoding.UTF8);
      var buffer = new char[8192];
      int read;
      while ((read = await reader.ReadAsync(
          buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        tail.Append(buffer.AsSpan(0, read));
      return tail.ToText();
    }

    /// <summary>Appends a line to <paramref name="output"/>, inserting a newline separator if it already has content.</summary>
    protected static void AppendOutputLine(TailText output, string line)
    {
      if (output.HasContent)
        output.Append(Environment.NewLine);
      output.Append(line.AsSpan());
    }

    /// <summary>Splits the accumulated tail text into individual lines (normalizing CRLF to LF).</summary>
    protected static List<string> ToOutputLines(TailText output)
    {
      var text = output.ToText();
      return string.IsNullOrEmpty(text)
          ? []
          : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
    }

    /// <summary>
    /// Fixed-capacity circular character buffer that retains only the most recent characters
    /// appended to it, used to cap CLI/API output captured for error messages without
    /// unbounded memory growth.
    /// </summary>
    /// <param name="maxChars">Maximum number of characters retained; older content is evicted first.</param>
    protected sealed class TailText(int maxChars)
    {
      private readonly char[] _buffer = new char[maxChars];
      private int _start;
      private int _count;
      private bool _truncated;

      /// <summary>True if any characters have been appended.</summary>
      public bool HasContent => _count > 0;

      /// <summary>Appends characters, evicting the oldest content once capacity is exceeded.</summary>
      public void Append(ReadOnlySpan<char> chars)
      {
        if (chars.Length > _buffer.Length)
        {
          chars[^_buffer.Length..].CopyTo(_buffer);
          _start = 0;
          _count = _buffer.Length;
          _truncated = true;
          return;
        }

        foreach (var c in chars)
        {
          if (_count < _buffer.Length)
          {
            _buffer[(_start + _count) % _buffer.Length] = c;
            _count++;
          }
          else
          {
            _buffer[_start] = c;
            _start = (_start + 1) % _buffer.Length;
            _truncated = true;
          }
        }
      }

      /// <summary>Renders the retained tail as text, prefixed with a truncation marker if content was evicted.</summary>
      public string ToText()
      {
        var chars = new char[_count];
        for (var i = 0; i < _count; i++)
          chars[i] = _buffer[(_start + i) % _buffer.Length];
        var text = new string(chars);
        return _truncated
            ? CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars) + Environment.NewLine + text
            : text;
      }
    }
  }
}
