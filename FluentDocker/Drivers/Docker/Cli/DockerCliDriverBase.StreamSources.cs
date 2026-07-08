using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    protected async IAsyncEnumerable<LogEntry> ExecuteStreamingCommandWithSourcesAsync(
        DriverContext context,
        string arguments,
        bool stdout,
        bool stderr,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var effectiveContext = CreateEffectiveContext(context);
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo(effectiveContext);
      var globalArgs = BuildGlobalArgs(effectiveContext);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, sudoPassword);

      using var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = processFileName,
          Arguments = processArguments,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          RedirectStandardInput = passwordForStdin != null,
          UseShellExecute = false,
          CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8,
          StandardErrorEncoding = Encoding.UTF8
        }
      };

      if (process.StartInfo.RedirectStandardInput)
        process.StartInfo.StandardInputEncoding = Utf8NoBom;

      StartProcessOrThrow(process, binaryPath);
      if (passwordForStdin != null)
        _ = await TryWriteStandardInputAsync(process, passwordForStdin, null, cancellationToken).ConfigureAwait(false);

      var channel = Channel.CreateBounded<LogEntry>(
          new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
      var tail = new Queue<string>();
      var pump = PumpSourceStreamsAsync(process, channel.Writer, stdout, stderr, tail, cancellationToken);
      string failure = null;

      try
      {
        await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
          yield return entry;

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
          failure = $"exit code {process.ExitCode}{FormatTail(tail)}";
      }
      finally
      {
        channel.Writer.TryComplete();
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(pump).ConfigureAwait(false);
      }

      if (failure != null)
        throw new DriverException($"Streaming command failed ({failure}).", ErrorCodes.Driver.CommandExecutionFailed);
    }

    private static async Task PumpSourceStreamsAsync(
        Process process,
        ChannelWriter<LogEntry> writer,
        bool stdout,
        bool stderr,
        Queue<string> tail,
        CancellationToken cancellationToken)
    {
      async Task PumpAsync(TextReader reader, LogStreamSource source, bool emit)
      {
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
          lock (tail)
            AddTail(tail, line);
          if (emit)
            await writer.WriteAsync(new LogEntry { Source = source, Line = line }, cancellationToken).ConfigureAwait(false);
        }
      }

      try
      {
        await Task.WhenAll(
            PumpAsync(process.StandardOutput, LogStreamSource.Stdout, stdout),
            PumpAsync(process.StandardError, LogStreamSource.Stderr, stderr)).ConfigureAwait(false);
        writer.TryComplete();
      }
      catch (Exception ex)
      {
        writer.TryComplete(ex);
      }
    }
  }
}
