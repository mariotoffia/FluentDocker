using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Podman.Cli
{
  public abstract partial class PodmanCliDriverBase
  {
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static async Task<Exception?> TryWriteStandardInputAsync(
        Process process,
        string? passwordForStdin,
        string? stdinData,
        CancellationToken cancellationToken)
    {
      try
      {
        if (passwordForStdin != null)
          await process.StandardInput.WriteLineAsync(passwordForStdin.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (stdinData != null)
          await process.StandardInput.WriteAsync(stdinData.AsMemory(), cancellationToken).ConfigureAwait(false);
        return null;
      }
      catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
      {
        return ex;
      }
      finally
      {
        try
        { process.StandardInput.Close(); }
        catch (Exception)
        { /* best effort */ }
      }
    }

    private static async Task<string?> TryReadStringTaskAsync(Task<string>? task)
    {
      if (task == null)
        return null;
      try
      {
        return await task.ConfigureAwait(false);
      }
      catch (Exception)
      {
        return null;
      }
    }

    private static async Task TryObserveTaskAsync(Task? task)
    {
      if (task == null)
        return;
      try
      {
        await task.ConfigureAwait(false);
      }
      catch
      {
        // best effort drain/observe
      }
    }

    /// <summary>
    /// Returns the last few KiB of captured process output for attaching to a diagnostic
    /// <see cref="FluentDocker.Model.Drivers.ErrorContext"/> — enough to see why a command
    /// hung/timed out without dragging a multi-MiB buffer into the exception.
    /// </summary>
    private static string? DiagnosticTail(string? text, int maxChars = 4096) =>
        string.IsNullOrEmpty(text) || text.Length <= maxChars ? text : text[^maxChars..];

    /// <summary>
    /// Snapshot of a reader sink after its (possibly cancelled) task has been awaited:
    /// the reader no longer appends at that point, so the read is race-free.
    /// </summary>
    private static string? SinkSnapshot(System.Text.StringBuilder? sink) =>
        sink is { Length: > 0 } ? sink.ToString() : null;

    private static int GetExitCodeOrDefault(Process? process)
    {
      try
      {
        return process?.HasExited == true ? process.ExitCode : -1;
      }
      catch (Exception)
      {
        return -1;
      }
    }
  }
}
