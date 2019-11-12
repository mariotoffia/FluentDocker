using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Ductus.FluentDocker.Common;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors
{
  public sealed class JsonStreamProcessExecutor
  {
    private readonly string _arguments;
    private readonly string _command;
    private readonly string _workingdir;

    internal JsonStreamProcessExecutor(string command, string arguments, string workingdir = null)
    {
      _command = command;
      _arguments = arguments;
      _workingdir = workingdir;
    }

    public ConsoleStream<JObject> Execute(CancellationToken token = default)
    {
      Logger.Log($"cmd: {_command} - arg: {_arguments}");
      return new ConsoleStream<JObject>(new ProcessStartInfo
      {
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        UseShellExecute = false,
        Arguments = _arguments,
        FileName = _command,
        WorkingDirectory = _workingdir
      }, new JsonStreamMapper(), token);
    }

    private sealed class JsonStreamMapper : IStreamMapper<JObject>
    {
      private readonly StringBuilder _sb = new StringBuilder();
      public string Error { get; private set; } = string.Empty;

      public JObject OnData(string data, bool isStdErr)
      {
        if (null == data)
          return null;

        var nl = data.IndexOf(Environment.NewLine);
        if (nl > -1)
        {
          var old = _sb.ToString();
          var n = data.Substring(0, nl);

          if (n.Length > nl)
            _sb.Append(data.Substring(nl));
          else
            _sb.Clear();

          return JObject.Parse(old + n);
        }

        _sb.Append(data);
        return null;
      }

      public JObject OnProcessEnd(int exitCode)
      {
        if (exitCode != 0)
          Error = $"Process exited with exit code {exitCode}";

        return _sb.Length > 0 ? JObject.Parse(_sb.ToString()) : null;
      }
    }
  }
}
