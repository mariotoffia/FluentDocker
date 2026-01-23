using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using FluentDocker.Common;
using FluentDocker.Model.Containers;

namespace FluentDocker.Executors
{
  public sealed class ProcessExecutor<T, TE> where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
  {
    private static string _defaultShell = "bash";
    private readonly string _arguments;
    private readonly string _command;
    private readonly string _workingdir;

    /// <summary>
    /// Gets or sets the default shell used for sudo/echo commands.
    /// </summary>
    public static string DefaultShell
    {
      get => _defaultShell;
      set => _defaultShell = value ?? "bash";
    }

    /// <summary>
    /// Sets the default shell for all ProcessExecutor instances.
    /// </summary>
    /// <param name="shell">The shell to use (e.g., "bash", "zsh").</param>
    public static void SetDefaultShell(string shell)
    {
      _defaultShell = shell ?? "bash";
    }

    public ProcessExecutor(string command, string arguments, string workingdir = null)
      : this(command, arguments, workingdir, _defaultShell)
    {
    }

    public ProcessExecutor(string command, string arguments, string workingdir, string shell)
    {
      _workingdir = workingdir;
      if (command.StartsWith("echo") || command.StartsWith("sudo"))
      {
        _command = shell ?? _defaultShell;
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
          startInfo.EnvironmentVariables[key] = Env[key];
        }

      Logger.Log($"cmd: {_command} - arg: {_arguments}");

      using (var process = new Process { StartInfo = startInfo })
      {
        var output = new StringBuilder();
        var err = new StringBuilder();

        process.OutputDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data))
            output.AppendLine(args.Data);
        };

        process.ErrorDataReceived += (sender, args) =>
        {
          if (!string.IsNullOrEmpty(args.Data))
            err.AppendLine(args.Data);
        };

        if (!process.Start())
          throw new FluentDockerException($"Could not start process {_command}");

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
