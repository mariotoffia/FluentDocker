using System.Collections.Generic;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;
using Newtonsoft.Json;

namespace FluentDocker.Executors.Parsers
{
  public sealed class VolumeInspectResponseParser : IProcessResponseParser<IList<Volume>>
  {
    public CommandResponse<IList<Volume>> Response { get; private set; }

    public IProcessResponse<IList<Volume>> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "No response", (IList<Volume>)new List<Volume>());
        return this;
      }

      var resp = JsonConvert.DeserializeObject<Volume[]>(response.StdOut);
      if (null == resp || 0 == resp.Length)
      {
        Response = response.ToResponse(false, "No response", (IList<Volume>)new List<Volume>());
        return this;
      }

      Response = response.ToResponse(true, string.Empty, (IList<Volume>)new List<Volume>(resp));
      return this;
    }
  }
}
