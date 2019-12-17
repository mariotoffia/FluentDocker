using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors
{
  public sealed class AsyncProcessExecutor<T, TE> where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
  {
    private readonly string _arguments;
    private readonly string _command;
    private readonly string _workingdir;

    public AsyncProcessExecutor(string command, string arguments, string workingdir = null)
    {
      _command = command;
      _arguments = arguments;
      _workingdir = workingdir;
    }

    public Task<CommandResponse<TE>> Execute(CancellationToken cancellationToken = default(CancellationToken))
    {
      return Task.Factory.StartNew(() => InternalExecute(cancellationToken), cancellationToken);
    }

    private CommandResponse<TE> InternalExecute(CancellationToken cancellationToken)
    {
      var startInfo = new ProcessStartInfo
      {
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        Arguments = _arguments,
        FileName = _command,
        WorkingDirectory = _workingdir
      };

      Logger.Log($"cmd: {_command} - arg: {_arguments}");

      using (var process = new Process { StartInfo = startInfo })
      {
        var output = new StringBuilder();
        var err = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data))
          {
            output.AppendLine(args.Data);
          }
        };

        process.ErrorDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data))
          {
            err.AppendLine(args.Data);
          }
        };

        if (!process.Start())
        {
          throw new FluentDockerException($"Could not start process {_command}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        while (!process.WaitForExit(1000))
        {
          cancellationToken.ThrowIfCancellationRequested();
        }

        return
          new T().Process(new ProcessExecutionResult(_command, output.ToString(), err.ToString(), process.ExitCode))
            .Response;
      }
    }
  }
}
