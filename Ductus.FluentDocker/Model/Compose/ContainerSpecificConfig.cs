using System;
using System.Collections.Generic;
using System.Net.Http;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Compose
{
  public sealed class ContainerSpecificConfig
  {
    /// <summary>
    /// Name of the container matching the compose file service name.
    /// </summary>
    public string Name { get; set; }    
    public Tuple<string /*portAndProto*/, long /*waitTimeout*/> WaitForPort { get; set; }
    public List<WaitForHttpParams> WaitForHttp { get; } = new List<WaitForHttpParams>();
    public Tuple<string /*process*/, long /*waitTimeout*/> WaitForProcess { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpToOnStart { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpFromOnDispose { get; set; }

    public Tuple<TemplateString /*host*/, bool /*explode*/,
      Func<IContainerService, bool> /*condition*/> ExportOnDispose { get; set; }

    public List<string> ExecuteOnRunningArguments { get; set; }
    public List<string> ExecuteOnDisposingArguments { get; set; }

    public sealed class WaitForHttpParams
    {
      public string Url { get; set; }
      public long Timeout { get; set; }
      public Func<RequestResponse, int, long> Continuation { get; set; }
      public HttpMethod Method { get; set; } 
      public string ContentType { get; set; } 
      public string Body { get; set; }
    }
  }
}