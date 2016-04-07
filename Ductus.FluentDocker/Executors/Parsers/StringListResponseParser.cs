using System.Collections.Generic;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class StringListResponseParser : IProcessResponseParser<IList<string>>
  {
    public IList<string> Response { get; private set; }

    public IProcessResponse<IList<string>> Process(string response)
    {
      var list = string.IsNullOrEmpty(response) ? new List<string>() : new List<string>(response.Split('\n'));
      list.RemoveAll(string.IsNullOrWhiteSpace);

      Response = list;
      return this;
    }
  }
}