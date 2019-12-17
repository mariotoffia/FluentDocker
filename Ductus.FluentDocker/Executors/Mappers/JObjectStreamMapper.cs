using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class JObjectStreamMapper : IStreamMapper<JObject>
  {
    public string Error { get; private set; } = string.Empty;

    public JObject OnData(string data, bool isStdErr)
    {
      if (null == data)
        return null;

      return JObject.Parse(data);
    }

    public JObject OnProcessEnd(int exitCode)
    {
      if (exitCode != 0)
        Error = $"Process exited with exit code {exitCode}";

      return null;
    }
  }
}
