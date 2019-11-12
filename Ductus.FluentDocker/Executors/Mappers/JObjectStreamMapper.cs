using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class JObjectStreamMapper : IStreamMapper<JObject>
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
