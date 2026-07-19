using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public sealed partial class MockDockerApiConnection
  {
    private readonly List<(string PathContains, Queue<(HttpStatusCode StatusCode, string JsonBody)> Responses)> _getSequences = [];

    public MockDockerApiConnection SetupGetSequence(
        string pathContains, params (int status, string json)[] responses)
    {
      _getSequences.Add((pathContains, new Queue<(HttpStatusCode StatusCode, string JsonBody)>(
          responses.Select(r => ((HttpStatusCode)r.status, r.json)))));
      return this;
    }

    private bool TryResolveGetSequence(string method, string path, out HttpResponseMessage response)
    {
      response = null!;
      if (method != "GET")
        return false;

      var entry = _getSequences
          .Where(e => path.Contains(e.PathContains))
          .LastOrDefault();
      if (entry == default)
        return false;

      var next = entry.Responses.Count > 1
          ? entry.Responses.Dequeue()
          : entry.Responses.Peek();
      var content = new TrackingContent(next.JsonBody, Encoding.UTF8, "application/json");
      _contents.Add(content);
      response = new HttpResponseMessage(next.StatusCode)
      {
        Content = content
      };
      return true;
    }
  }
}
