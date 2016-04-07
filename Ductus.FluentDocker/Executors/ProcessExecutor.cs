using System.Diagnostics;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker.Executors
{
  public sealed class ProcessExecutor<T,TE> where T : IProcessResponseParser<TE>, IProcessResponse<TE>, new()
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

    public TE Execute()
    {
      var startInfo = new ProcessStartInfo
      {
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        Arguments = _command,
        FileName = _app,
        WorkingDirectory = _workingdir
      };

      Debugger.Log((int)TraceLevel.Verbose, Constants.DebugCategory, $"app: {_app} - command: {_command}");

      var process = new Process {StartInfo = startInfo};
      if (!process.Start())
      {
        throw new FluentDockerException($"Could not start process {_app}");
      }

      var s = process.StandardOutput.ReadToEnd();

      return new T().Process(s).Response;
    }
  }
}