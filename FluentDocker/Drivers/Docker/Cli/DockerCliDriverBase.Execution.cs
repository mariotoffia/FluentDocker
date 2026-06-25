using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Command-execution half of <see cref="DockerCliDriverBase"/> (process spawning,
  /// bounded buffered reads, streaming, and attach). Split into its own partial file
  /// purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class DockerCliDriverBase
  {
    #region Command Execution

    /// <summary>
    /// Sanity cap (4 MiB) on the buffered stdout of a non-streaming Docker command.
    /// Normal model/system command output (e.g. <c>docker model ls --json</c>) is tiny;
    /// this only guards against pathological CLI output being buffered without limit
    /// (which would otherwise risk an out-of-memory condition). On exceeding the cap the
    /// command fails with a clear error rather than continuing to allocate. Streaming
    /// commands are unaffected — they are read line-by-line and never fully buffered.
    /// </summary>
    private const int MaxNonStreamingOutputBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Resolves the binary info for the Docker command, extracting
    /// the binary path and sudo configuration separately for safe execution.
    /// </summary>
    private (string BinaryPath, SudoMechanism Sudo, string SudoPassword) ResolveBinaryInfo()
    {
      if (BinaryResolver == null)
        return (DockerCommand, SudoMechanism.None, null);

      var binary = BinaryResolver.Resolve(DockerCommand);
      return (binary.FqPath, binary.Sudo, binary.SudoPassword);
    }

    /// <summary>
    /// Executes a Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command result</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(string arguments, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, null, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, null, stdinData, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with additional environment variables.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(binaryPath, fullArgs, environment, null, sudo, sudoPassword, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// Handles sudo by setting the process FileName to "sudo" and passing the
    /// password via stdin (never on the command line).
    /// </summary>
    private static async Task<SimpleCommandResult> ExecuteProcessAsync(
        string fileName, string arguments,
        IDictionary<string, string> environment,
        string stdinData,
        SudoMechanism sudo, string sudoPassword,
        CancellationToken cancellationToken)
    {
      // Build the actual process command based on sudo mechanism.
      // The password is NEVER placed on the command line.
      var (processFileName, processArguments, passwordForStdin) =
          BuildSudoCommand(fileName, arguments, sudo, sudoPassword);

      var needsStdin = stdinData != null || passwordForStdin != null;

      Process process = null;
      try
      {
        process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = processFileName,
            Arguments = processArguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = needsStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
          }
        };

        if (environment != null)
        {
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;
        }

        process.Start();

        if (needsStdin)
        {
          // Write sudo password first (if any), then caller data.
          if (passwordForStdin != null)
            await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);

          if (stdinData != null)
            await process.StandardInput.WriteAsync(stdinData).ConfigureAwait(false);

          process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlock
        // when either pipe buffer fills up. Stdout is bounded by a sanity cap so a
        // pathological child cannot force unbounded buffering; stderr is read in full
        // (it is only used for failure messages and is trimmed on use).
        var outputTask = ReadBoundedAsync(process.StandardOutput, MaxNonStreamingOutputBytes, cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new SimpleCommandResult
        {
          Success = process.ExitCode == 0,
          Output = output,
          Error = error,
          ExitCode = process.ExitCode
        };
      }
      catch (OperationCanceledException)
      {
        // Kill the child process on cancellation to prevent orphans.
        KillProcessSafely(process);
        throw;
      }
      catch (Exception ex)
      {
        try
        { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch { /* best effort — process may have exited between the check and the kill */ }
        return new SimpleCommandResult
        {
          Success = false,
          Error = ex.Message,
          ExitCode = -1
        };
      }
      finally
      {
        process?.Dispose();
      }
    }

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
    /// Executes a streaming Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of output lines</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
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

      process.Start();

      if (passwordForStdin != null)
      {
        await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);
        process.StandardInput.Close();
      }

      // Drain stderr concurrently so a chatty child cannot deadlock by filling the
      // stderr pipe buffer while we only read stdout.
      var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
      var reader = process.StandardOutput;
      string failure = null;

      try
      {
        string line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
          yield return line;

        // Stdout reached EOF — wait for the process and surface a non-zero exit as a
        // failure rather than ending the stream silently (a failed pull/logs must throw).
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
          var trimmed = (error ?? string.Empty).Trim();
          if (trimmed.Length > 2000)
            trimmed = trimmed[..2000] + "…";
          failure = $"exit code {process.ExitCode}{(trimmed.Length == 0 ? string.Empty : $": {trimmed}")}";
        }
      }
      finally
      {
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(errorTask).ConfigureAwait(false);
      }

      if (failure != null)
        throw new DriverException($"Streaming command failed ({failure}).", ErrorCodes.Driver.CommandExecutionFailed);
    }

    /// <summary>
    /// Executes a streaming Docker command that emits its progress on <b>stderr</b>
    /// (e.g. <c>docker model pull</c>), interleaving stdout and stderr lines into a
    /// single ordered-by-arrival sequence so progress text is actually observed.
    /// <para>
    /// This is intentionally a separate path from
    /// <see cref="ExecuteStreamingCommandAsync(string, CancellationToken)"/>, which yields
    /// only stdout and is used by logs/events/stats consumers that must NOT have stderr
    /// folded into their output. A non-zero exit is surfaced as a
    /// <see cref="DriverException"/> exactly as the stdout-only variant does.
    /// </para>
    /// </summary>
    /// <param name="arguments">Command arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stdout and stderr lines, in arrival order.</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandWithProgressAsync(
        string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var (binaryPath, sudo, sudoPassword) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
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

      process.Start();

      if (passwordForStdin != null)
      {
        await process.StandardInput.WriteLineAsync(passwordForStdin).ConfigureAwait(false);
        process.StandardInput.Close();
      }

      // Both pipes are read line-by-line and merged into a bounded channel so neither
      // can deadlock by filling its pipe buffer while only the other is consumed.
      var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(
          new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

      var pump = PumpBothStreamsAsync(process, channel.Writer, cancellationToken);
      string failure = null;

      try
      {
        await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
          yield return line;

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
          failure = $"exit code {process.ExitCode}";
      }
      finally
      {
        KillProcessSafely(process, Logger);
        await ObserveQuietlyAsync(pump).ConfigureAwait(false);
      }

      if (failure != null)
        throw new DriverException($"Streaming command failed ({failure}).", ErrorCodes.Driver.CommandExecutionFailed);
    }

    /// <summary>
    /// Reads stdout and stderr concurrently, writing every line to <paramref name="writer"/>
    /// in arrival order, then completes the writer when both streams reach EOF.
    /// </summary>
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

    /// <summary>Awaits a task, swallowing any fault — used to observe a best-effort
    /// background read (e.g. stderr drain) when a stream is torn down early.</summary>
    private static async Task ObserveQuietlyAsync(Task task)
    {
      try
      {
        await task.ConfigureAwait(false);
      }
      catch (Exception)
      {
        // The stream is ending (early break/cancel); the drain result is irrelevant.
      }
    }

    /// <summary>
    /// Starts a long-running attach process with stdin/stdout/stderr redirected.
    /// </summary>
    protected AttachResult ExecuteAttachProcess(string arguments)
    {
      var (binaryPath, sudo, _) = ResolveBinaryInfo();
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      // Attach does not support sudo with password (would conflict with stdin).
      var (processFileName, processArguments, _) =
          BuildSudoCommand(binaryPath, fullArgs, sudo, null);

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = processFileName,
          Arguments = processArguments,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8,
          StandardErrorEncoding = Encoding.UTF8
        }
      };

      process.Start();

      return new AttachResult
      {
        InputStream = process.StandardInput.BaseStream,
        OutputStream = process.StandardOutput.BaseStream,
        ErrorStream = process.StandardError.BaseStream,
        IsConnected = true,
        AttachedProcess = process
      };
    }

    #endregion
  }
}
