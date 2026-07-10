using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads a text stream line by line while bounding the memory a single line can consume.
  /// A pathological line without a newline (a huge JSON event, an app dumping a blob into
  /// its logs) would otherwise buffer indefinitely in a <see cref="StringBuilder"/> during a
  /// long-running <c>logs -f</c> / <c>events</c> / <c>stats</c> observer. Once a line exceeds
  /// <see cref="MaxStreamingLineChars"/> the remainder is discarded and a marker appended.
  /// Handles CR, LF and CRLF line endings across read boundaries.
  /// </summary>
  /// <remarks>
  /// Shared by the Docker and Podman CLI bases so the hardening cannot drift between them.
  /// </remarks>
  internal sealed class BoundedLineReader(TextReader reader)
  {
    /// <summary>Per-line character cap (1 MiB) before truncation kicks in.</summary>
    internal const int MaxStreamingLineChars = 1024 * 1024;

    /// <summary>
    /// Suffix appended to a line that was truncated at <see cref="MaxStreamingLineChars"/>.
    /// Exposed so structured-line consumers (e.g. multi-line JSON accumulators) can detect a
    /// truncated — hence unparseable — line and reset instead of wedging on unbalanced brackets.
    /// </summary>
    internal const string TruncationMarker = "…[line truncated at 1,048,576 chars]";

    private readonly TextReader _reader = reader;
    private readonly char[] _buffer = new char[8192];
    private readonly StringBuilder _line = new();
    private int _index;
    private int _count;
    private bool _skipLeadingLf;

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
        _line.Append(TruncationMarker);
      return _line.ToString();
    }
  }
}
