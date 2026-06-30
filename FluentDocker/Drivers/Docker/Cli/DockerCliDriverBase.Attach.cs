using System.Diagnostics;
using System.Text;
using System.Threading;
using FluentDocker.Drivers;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Attach half of <see cref="DockerCliDriverBase"/> (long-lived <c>docker attach</c>
  /// process spawning). Split into its own partial file purely to keep each source file
  /// within the repository's 500-line limit.
  /// </summary>
  public abstract partial class DockerCliDriverBase
  {
    /// <summary>
    /// Starts a long-running attach process with stdin/stdout/stderr redirected.
    /// </summary>
    /// <param name="arguments">The CLI arguments for the attach command.</param>
    /// <param name="cancellationToken">
    /// Token observed before the process is started; if cancellation is already requested the
    /// process is never spawned. The attach itself is long-lived and is torn down by disposing
    /// the returned <see cref="AttachResult"/>.
    /// </param>
    protected AttachResult ExecuteAttachProcess(string arguments, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();

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
  }
}
