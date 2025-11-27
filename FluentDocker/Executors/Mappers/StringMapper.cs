using System;

namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class StringMapper : IStreamMapper<string>
  {
    public string OnData(string data, bool isStdErr)
    {
      return data;
    }

    public string OnProcessEnd(int exitCode)
    {
      if (exitCode != 0)
      {
        Error = $"Process exited with exit code {exitCode}";
      }

      return null;
    }

    public string Error { get; private set; } = string.Empty;
  }
}
