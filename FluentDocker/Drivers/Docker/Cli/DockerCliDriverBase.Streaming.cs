using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    private const int MaxStreamingLineChars = 1024 * 1024;
    private const string StreamingLineTruncatedMarker = "…[line truncated at 1,048,576 chars]";

    private static void AddTail(Queue<string> tail, string line)
    {
      if (tail.Count == 10)
        tail.Dequeue();
      tail.Enqueue(line);
    }

    private static string FormatTail(Queue<string> tail)
    {
      if (tail.Count == 0)
        return string.Empty;

      var text = string.Join(Environment.NewLine, tail).Trim();
      if (text.Length > 2000)
        text = text[^2000..];
      return $": {text}";
    }

    private static async Task PumpBothStreamsAsync(
        Process process, System.Threading.Channels.ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
      async Task PumpAsync(TextReader reader)
      {
        var lineReader = new BoundedLineReader(reader);
        string line;
        while ((line = await lineReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          await writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
      }

      try
      {
        await Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError)).ConfigureAwait(false);
        writer.TryComplete();
      }
      catch (Exception ex)
      {
        writer.TryComplete(ex);
      }
    }

    private static async Task ObserveQuietlyAsync(Task task)
    {
      try
      {
        await task.ConfigureAwait(false);
      }
      catch (Exception)
      {
        // The stream is ending; the drain result is irrelevant.
      }
    }

    private sealed class BoundedLineReader
    {
      private readonly TextReader _reader;
      private readonly char[] _buffer = new char[8192];
      private readonly StringBuilder _line = new();
      private int _index;
      private int _count;
      private bool _skipLeadingLf;

      public BoundedLineReader(TextReader reader) => _reader = reader;

      public async Task<string> ReadLineAsync(CancellationToken cancellationToken)
      {
        _line.Clear();
        var sawAny = false;
        var truncated = false;

        while (true)
        {
          if (_index >= _count)
          {
            _count = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            _index = 0;
            if (_count == 0)
              return sawAny ? Finish(truncated) : null;
          }

          var ch = _buffer[_index++];
          if (_skipLeadingLf)
          {
            _skipLeadingLf = false;
            if (ch == '\n')
              continue;
          }

          sawAny = true;
          if (ch == '\r')
          {
            _skipLeadingLf = true;
            return Finish(truncated);
          }
          if (ch == '\n')
            return Finish(truncated);

          if (_line.Length < MaxStreamingLineChars)
            _line.Append(ch);
          else
            truncated = true;
        }
      }

      private string Finish(bool truncated)
      {
        if (truncated)
          _line.Append(StreamingLineTruncatedMarker);
        return _line.ToString();
      }
    }
  }
}
