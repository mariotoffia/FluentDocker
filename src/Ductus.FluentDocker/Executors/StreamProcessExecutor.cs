using System.Diagnostics;
using System.Threading;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Executors
{
  public sealed class StreamProcessExecutor<T, TE> where T : class, IStreamMapper<TE>, new()
    where TE : class
  {
    private readonly string _arguments;
    private readonly string _command;
    private readonly string _workingdir;

    public StreamProcessExecutor(string command, string arguments, string workingdir = null)
    {
      _command = command;
      _arguments = arguments;
      _workingdir = workingdir;
    }

    public ConsoleStream<TE> Execute(CancellationToken token = default(CancellationToken))
    {
      Logger.Log($"cmd: {_command} - arg: {_arguments}");
      return new ConsoleStream<TE>(new ProcessStartInfo
      {
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        Arguments = _arguments,
        FileName = _command,
        WorkingDirectory = _workingdir
      }, new T(), token);
    }
  }
}