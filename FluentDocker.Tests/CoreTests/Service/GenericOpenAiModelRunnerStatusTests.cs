using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
using FluentDocker.Services.Impl;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class GenericOpenAiModelRunnerStatusTests
  {
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task StatusAsync_ModelListPathNotFound_ReturnsNotRunningWithPathHint(HttpStatusCode statusCode)
    {
      using var handler = new FuncHandler(_ => Json(statusCode, "{\"error\":\"wrong path\"}"));
      await using var connection = new ModelApiConnection(new Uri("http://localhost:12434/wrong"), handler);
      var endpoint = ModelRunnerEndpoint.Raw(new Uri("http://localhost:12434/wrong"));
      var inference = new OpenAiModelInferenceDriver(connection, endpoint);
      await using var runner = new GenericOpenAiModelRunner(endpoint, default!, inference, connection.PingAsync);

      var status = await runner.StatusAsync(TestContext.Current.CancellationToken);

      Assert.False(status.Running);
      Assert.Contains(((int)statusCode).ToString(CultureInfo.InvariantCulture), status.Error, StringComparison.Ordinal);
      Assert.Contains("/wrong/models", status.Error, StringComparison.Ordinal);
      Assert.Contains("base path", status.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FuncHandler : HttpMessageHandler
    {
      private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

      public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
          Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
  }
}
