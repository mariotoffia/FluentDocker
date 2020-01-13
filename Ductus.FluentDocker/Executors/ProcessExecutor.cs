using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors
{
  public sealed class ProcessExecutor<T, TE> where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
  {
    private readonly string _arguments;
    private readonly string _command;
    private readonly string _workingdir;

    public ProcessExecutor(string command, string arguments, string workingdir = null)
    {
      _workingdir = workingdir;
      if (command.StartsWith("echo") || command.StartsWith("sudo"))
      {
        _command = CommandExtensions.DefaultShell;
        _arguments = $"-c \"{command} {arguments}\"";

        return;
      }

      _command = command;
      _arguments = arguments;
    }

    public IDictionary<string, string> Env { get; } = new Dictionary<string, string>();

    public CommandResponse<TE> Execute()
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

      if (0 != Env.Count)
        foreach (var key in Env.Keys)
        {
#if COREFX
          startInfo.Environment[key] = Env[key];
#else
          startInfo.EnvironmentVariables[key] = Env[key];
#endif
        }

      Logger.Log($"cmd: {_command} - arg: {_arguments}");

      using (var process = new Process {StartInfo = startInfo})
      {
        var output = new StringBuilder();
        var err = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data)) output.AppendLine(args.Data);
        };

        process.ErrorDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data)) err.AppendLine(args.Data);
        };

        if (!process.Start()) throw new FluentDockerException($"Could not start process {_command}");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        return
          new T().Process(new ProcessExecutionResult(_command, output.ToString(), err.ToString(), process.ExitCode))
            .Response;
      }
    }
  }
}
