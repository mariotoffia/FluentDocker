namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class StringProcessResponseParser : IProcessResponseParser<string>
  {
    public IProcessResponse<string> Process(string response)
    {
      Response = response;
      return this;
    }

    public string Response { get; private set; }
  }
}
