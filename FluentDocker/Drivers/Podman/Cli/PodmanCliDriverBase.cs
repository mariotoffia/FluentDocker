using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  /// <summary>
  /// Base class for Podman CLI driver components.
  /// Provides shared command execution functionality.
  /// </summary>
  public abstract class PodmanCliDriverBase
  {
    /// <summary>
    /// The Podman command executable name.
    /// </summary>
    protected const string PodmanCommand = "podman";

    /// <summary>
    /// The driver context.
    /// </summary>
    protected DriverContext Context { get; private set; }

    /// <summary>
    /// The binary resolver for resolving Podman command paths.
    /// </summary>
    protected IPodmanBinaryResolver BinaryResolver { get; private set; }

    /// <summary>
    /// Creates a new instance without a binary resolver.
    /// </summary>
    protected PodmanCliDriverBase()
    {
    }

    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    protected PodmanCliDriverBase(IPodmanBinaryResolver binaryResolver) => BinaryResolver = binaryResolver;

    /// <summary>
    /// Initializes the driver component with the given context.
    /// </summary>
    public virtual void Initialize(DriverContext context)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Initializes the driver component with the given context and binary resolver.
    /// </summary>
    public virtual void Initialize(DriverContext context, IPodmanBinaryResolver binaryResolver)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      BinaryResolver = binaryResolver ?? throw new ArgumentNullException(nameof(binaryResolver));
    }

    #region Global Args

    /// <summary>
    /// Builds global CLI flags from the driver context.
    /// Podman uses --url for remote host. TLS certificate flags are not
    /// supported via the Podman CLI, so <see cref="DriverContext.CertificatePath"/>
    /// is ignored.
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <returns>A string of global flags to prepend to Podman commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context)
    {
      if (context == null || string.IsNullOrEmpty(context.Host))
        return "";

      return $"--url {context.Host}";
    }

    #endregion

    #region Command Execution

    /// <summary>
    /// Executes a Podman command asynchronously.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, CancellationToken cancellationToken)
    {
      var podmanPath = BinaryResolver?.ResolveBinaryPath(PodmanCommand) ?? PodmanCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(podmanPath, fullArgs, null, cancellationToken);
    }

    /// <summary>
    /// Executes a Podman command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var podmanPath = BinaryResolver?.ResolveBinaryPath(PodmanCommand) ?? PodmanCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(podmanPath, fullArgs, stdinData, cancellationToken);
    }

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteProcessAsync(
        string fileName, string arguments,
        string stdinData, CancellationToken cancellationToken)
    {
      try
      {
        var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData != null,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();

        if (stdinData != null)
        {
          await process.StandardInput.WriteAsync(stdinData);
          process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlock
        // when either pipe buffer fills up.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        // Ensure process has fully exited and get exit code.
        await process.WaitForExitAsync(cancellationToken);

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
        throw;
      }
      catch (Exception ex)
      {
        return new SimpleCommandResult
        {
          Success = false,
          Error = ex.Message,
          ExitCode = -1
        };
      }
    }

    /// <summary>
    /// Executes a streaming Podman command asynchronously.
    /// </summary>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(
        string arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var podmanPath = BinaryResolver?.ResolveBinaryPath(PodmanCommand) ?? PodmanCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = podmanPath,
          Arguments = fullArgs,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        }
      };

      process.Start();

      var reader = process.StandardOutput;

      while (!cancellationToken.IsCancellationRequested)
      {
        var line = await reader.ReadLineAsync();
        if (line == null)
          break;

        yield return line;
      }

      if (!process.HasExited)
      {
        try
        {
          process.Kill();
        }
        catch
        {
          // Ignore kill errors
        }
      }
    }

    /// <summary>
    /// Starts a long-running attach process with stdin/stdout/stderr redirected.
    /// </summary>
    protected AttachResult ExecuteAttachProcess(string arguments)
    {
      var podmanPath = BinaryResolver?.ResolveBinaryPath(PodmanCommand) ?? PodmanCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = podmanPath,
          Arguments = fullArgs,
          RedirectStandardInput = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
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

    #region Error Context

    /// <summary>
    /// Creates an error context from a command result.
    /// </summary>
    protected ErrorContext CreateErrorContext(
        DriverContext context, string operation, SimpleCommandResult result)
    {
      return new ErrorContext(operation)
      {
        DriverId = context.DriverId,
        Host = context.Host,
        ExitCode = result.ExitCode,
        StdOut = result.Output,
        StdErr = result.Error
      };
    }

    /// <summary>
    /// Creates an error context using the component's context.
    /// </summary>
    protected ErrorContext CreateErrorContext(string operation, SimpleCommandResult result)
    {
      return CreateErrorContext(Context, operation, result);
    }

    #endregion

    #region Argument Quoting

    /// <summary>
    /// Quotes a command-line argument if it contains shell metacharacters or whitespace.
    /// Escapes backslashes and double quotes within the argument.
    /// </summary>
    protected static string QuoteArgumentIfNeeded(string argument)
    {
      if (string.IsNullOrEmpty(argument))
        return "\"\"";

      var needsQuoting = argument.IndexOfAny(new[] { ' ', '\t', ';', '&', '|', '>', '<', '"', '\'' }) >= 0;
      if (!needsQuoting)
        return argument;

      var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
      return $"\"{escaped}\"";
    }

    #endregion
  }
}
