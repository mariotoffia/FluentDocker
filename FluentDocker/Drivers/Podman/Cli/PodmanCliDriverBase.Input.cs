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

    private static async Task<Exception> TryWriteStandardInputAsync(
        Process process,
        string passwordForStdin,
        string stdinData,
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

    private static async Task<string> TryReadStringTaskAsync(Task<string> task)
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

    private static async Task TryObserveTaskAsync(Task task)
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

    private static int GetExitCodeOrDefault(Process process)
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
