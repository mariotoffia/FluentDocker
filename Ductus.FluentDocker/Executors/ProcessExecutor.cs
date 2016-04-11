using System.Diagnostics;
using System.Text;
using Ductus.FluentDocker.Internal;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors
{
  public sealed class ProcessExecutor<T, TE> where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
  {
    private readonly string _app;
    private readonly string _command;
    private readonly string _workingdir;

    public ProcessExecutor(string app, string commad, string workingdir = null)
    {
      _app = app;
      _command = commad;
      _workingdir = workingdir;
    }

    public CommandResponse<TE> Execute()
    {
      var startInfo = new ProcessStartInfo
      {
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        Arguments = _command,
        FileName = _app,
        WorkingDirectory = _workingdir
      };


      Debugger.Log((int) TraceLevel.Verbose, Constants.DebugCategory, $"app: {_app} - command: {_command}");

      using (var process = new Process {StartInfo = startInfo})
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
          throw new FluentDockerException($"Could not start process {_app}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        return
          new T().Process(new ProcessExecutionResult(process, output.ToString(), err.ToString(), process.ExitCode))
            .Response;
      }
    }
  }
}