namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class SingleStringResponseParser : IProcessResponseParser<string>
  {
    public string Response { get; private set; }
    public IProcessResponse<string> Process(string response)
    {
      var s = response.Split('\n');
      Response = s[0];
      return this;
    }
  }
}
