namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class StringMapper : IStreamMapper<string>
  {
    private string _error = string.Empty;
    public string OnData(string data, bool isStdErr)
    {
      if (!isStdErr)
      {
        return data;
      }

      _error += data;
      return null;
    }
    public string OnProcessEnd(int exitCode)
    {
      return null;
    }
    public string Error => _error;
  }
}
