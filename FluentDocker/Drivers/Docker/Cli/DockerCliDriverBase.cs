using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Base class for Docker CLI driver components.
  /// Provides shared command execution functionality.
  /// </summary>
  public abstract class DockerCliDriverBase
  {
    /// <summary>
    /// The Docker command executable name.
    /// </summary>
    protected const string DockerCommand = "docker";

    /// <summary>
    /// The driver context.
    /// </summary>
    protected DriverContext Context { get; private set; }

    /// <summary>
    /// The binary resolver for resolving Docker command paths.
    /// </summary>
    protected IBinaryResolver BinaryResolver { get; private set; }

    /// <summary>
    /// Creates a new instance without a binary resolver.
    /// </summary>
    protected DockerCliDriverBase()
    {
    }

    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    /// <param name="binaryResolver">The binary resolver to use.</param>
    protected DockerCliDriverBase(IBinaryResolver binaryResolver) => BinaryResolver = binaryResolver;

    /// <summary>
    /// Initializes the driver component with the given context.
    /// </summary>
    /// <param name="context">Driver context</param>
    public virtual void Initialize(DriverContext context)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Initializes the driver component with the given context and binary resolver.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="binaryResolver">Binary resolver</param>
    public virtual void Initialize(DriverContext context, IBinaryResolver binaryResolver)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      BinaryResolver = binaryResolver ?? throw new ArgumentNullException(nameof(binaryResolver));
    }

    #region Global Args

    /// <summary>
    /// Builds global CLI flags from the driver context, including host (-H)
    /// and TLS certificate flags (--tlsverify/--tls, --tlscacert, --tlscert, --tlskey).
    /// </summary>
    /// <param name="context">The driver context (may be null).</param>
    /// <returns>A string of global flags to prepend to Docker commands, or empty string.</returns>
    public static string BuildGlobalArgs(DriverContext context)
    {
      if (context == null || string.IsNullOrEmpty(context.Host))
        return "";

      var sb = new StringBuilder();
      sb.Append($"-H {context.Host}");

      if (!string.IsNullOrEmpty(context.CertificatePath))
      {
        var certPath = context.CertificatePath;
        var caCert = Path.Combine(certPath, "ca.pem");
        var cert = Path.Combine(certPath, "cert.pem");
        var key = Path.Combine(certPath, "key.pem");

        if (context.VerifyTls)
          sb.Append(" --tlsverify");
        else
          sb.Append(" --tls");

        sb.Append($" --tlscacert {caCert} --tlscert {cert} --tlskey {key}");
      }

      return sb.ToString();
    }

    #endregion

    #region Command Execution

    /// <summary>
    /// Executes a Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command result</returns>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(string arguments, CancellationToken cancellationToken)
    {
      var dockerPath = BinaryResolver?.ResolveBinaryPath(DockerCommand) ?? DockerCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(dockerPath, fullArgs, null, null, cancellationToken);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with data piped to stdin.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments, string stdinData, CancellationToken cancellationToken)
    {
      var dockerPath = BinaryResolver?.ResolveBinaryPath(DockerCommand) ?? DockerCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(dockerPath, fullArgs, null, stdinData, cancellationToken);
    }

    /// <summary>
    /// Executes a Docker command asynchronously with additional environment variables.
    /// </summary>
    protected async Task<SimpleCommandResult> ExecuteCommandAsync(
        string arguments,
        IDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
      var dockerPath = BinaryResolver?.ResolveBinaryPath(DockerCommand) ?? DockerCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";
      return await ExecuteProcessAsync(dockerPath, fullArgs, environment, null, cancellationToken);
    }

    /// <summary>
    /// Executes a process asynchronously using direct stream reading
    /// to avoid event-based output race conditions.
    /// </summary>
    private async Task<SimpleCommandResult> ExecuteProcessAsync(
        string fileName, string arguments,
        IDictionary<string, string> environment,
        string stdinData,
        CancellationToken cancellationToken)
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

        if (environment != null)
        {
          foreach (var kvp in environment)
            process.StartInfo.Environment[kvp.Key] = kvp.Value;
        }

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
    /// Executes a streaming Docker command asynchronously.
    /// </summary>
    /// <param name="arguments">Command arguments</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of output lines</returns>
    protected async IAsyncEnumerable<string> ExecuteStreamingCommandAsync(string arguments, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      var dockerPath = BinaryResolver?.ResolveBinaryPath(DockerCommand) ?? DockerCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = dockerPath,
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
      var dockerPath = BinaryResolver?.ResolveBinaryPath(DockerCommand) ?? DockerCommand;
      var globalArgs = BuildGlobalArgs(Context);
      var fullArgs = string.IsNullOrEmpty(globalArgs) ? arguments : $"{globalArgs} {arguments}";

      var process = new Process
      {
        StartInfo = new ProcessStartInfo
        {
          FileName = dockerPath,
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
    /// <param name="context">Driver context</param>
    /// <param name="operation">Operation name</param>
    /// <param name="result">Command result</param>
    /// <returns>Error context</returns>
    protected ErrorContext CreateErrorContext(DriverContext context, string operation, SimpleCommandResult result)
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
    /// <param name="operation">Operation name</param>
    /// <param name="result">Command result</param>
    /// <returns>Error context</returns>
    protected ErrorContext CreateErrorContext(string operation, SimpleCommandResult result)
    {
      return CreateErrorContext(Context, operation, result);
    }

    #endregion

    #region Argument Quoting

    /// <summary>
    /// Quotes a command-line argument if it contains spaces or tabs.
    /// Escapes backslashes and double quotes within the argument.
    /// </summary>
    /// <param name="argument">The argument to potentially quote</param>
    /// <returns>The argument, quoted if necessary</returns>
    protected static string QuoteArgumentIfNeeded(string argument)
    {
      if (string.IsNullOrEmpty(argument))
        return argument;

      var needsQuoting = argument.Contains(' ') || argument.Contains('\t');

      if (!needsQuoting)
        return argument;

      var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
      return $"\"{escaped}\"";
    }

    #endregion
  }

  /// <summary>
  /// Result of a simple command execution.
  /// </summary>
  public class SimpleCommandResult
  {
    /// <summary>
    /// Whether the command succeeded (exit code 0).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Standard output from the command.
    /// </summary>
    public string Output { get; set; }

    /// <summary>
    /// Standard error from the command.
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Exit code from the command.
    /// </summary>
    public int ExitCode { get; set; }
  }
}

