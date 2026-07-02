using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
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
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
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
  }
}
