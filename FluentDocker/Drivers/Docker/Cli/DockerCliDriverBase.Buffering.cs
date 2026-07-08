using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Bounded buffered-read helpers for <see cref="DockerCliDriverBase"/>. Split into its
  /// own partial file purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class DockerCliDriverBase
  {
    /// <summary>
    /// Reads a text stream to end, failing fast once <paramref name="maxBytes"/> worth
    /// of characters has been buffered. This bounds the memory a single non-streaming
    /// command can consume; the cap is generous enough that any legitimate CLI output
    /// fits well within it.
    /// </summary>
    /// <exception cref="DriverException">Thrown when the output exceeds the cap.</exception>
    private static async Task<string> ReadBoundedAsync(TextReader reader, int maxBytes, CancellationToken cancellationToken)
    {
      // One UTF-16 char is at least one byte; capping the char count at maxBytes is a
      // safe (slightly conservative) upper bound on the byte size and avoids re-encoding.
      var sb = new StringBuilder();
      var buffer = new char[8192];
      int read;
      while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
      {
        if (sb.Length + read > maxBytes)
          throw new DriverException(
              $"Command output exceeded the {maxBytes}-byte limit.",
              ErrorCodes.Driver.CommandExecutionFailed);

        sb.Append(buffer, 0, read);
      }

      return sb.ToString();
    }

    /// <summary>
    /// Reads a text stream to end, keeping at most <paramref name="maxBytes"/> worth of
    /// characters and discarding (but still draining) the remainder. Used for <b>stderr</b>:
    /// unlike <see cref="ReadBoundedAsync"/> it never throws, because stderr carries the
    /// error message that callers surface — truncating preserves the head of that message
    /// while still bounding memory. The stream is drained to EOF even after the cap so the
    /// child process cannot block on a full stderr pipe.
    /// </summary>
    private static async Task<string> ReadBoundedTruncatingAsync(TextReader reader, int maxBytes, CancellationToken cancellationToken)
    {
      var sb = new StringBuilder();
      var buffer = new char[8192];
      var truncated = false;
      int read;
      while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
      {
        if (truncated)
          continue; // keep draining the pipe so the child can exit, but stop appending

        var remaining = maxBytes - sb.Length;
        if (read > remaining)
        {
          sb.Append(buffer, 0, remaining);
          truncated = true;
        }
        else
        {
          sb.Append(buffer, 0, read);
        }
      }

      if (truncated)
        sb.Append(CultureInfo.InvariantCulture, $"\n[stderr truncated at the {FormatByteCap(maxBytes)} cap]");

      return sb.ToString();
    }

    private static string FormatByteCap(int maxBytes)
    {
      var mib = maxBytes / (1024 * 1024);
      return maxBytes % (1024 * 1024) == 0
          ? FormattableString.Invariant($"{mib} MiB")
          : FormattableString.Invariant($"{maxBytes} bytes");
    }
  }
}
