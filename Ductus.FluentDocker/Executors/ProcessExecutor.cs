using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Ductus.FluentDocker.AmbientContext;
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

    public CommandResponse<TE> Execute([CallerMemberName] string caller = "")
    {
      Logger.Log($"cmd: {_command} - arg: {_arguments}");

      var pm = ProcessManagerContext.ProcessManager;

      var output = new StringBuilder();
      var err = new StringBuilder();

      pm.StandartTextReceived += (sender, s) =>
      {
        if (!string.IsNullOrEmpty(s))
          output.Append(s);
      };

      pm.ErrorTextReceived += (sender, s) =>
      {
        if (!string.IsNullOrEmpty(s))
          err.Append(s);
      };

      pm.ExecuteAsync(caller, _command, _workingdir, Env, _arguments);

      if (!pm.Running)
        throw new FluentDockerException($"Could not start process {_command}");

      pm.WaitForExit();

      return
        new T().Process(new ProcessExecutionResult(_command, output.ToString(), err.ToString(), pm.ExitCode))
          .Response;
    }
  }
}
