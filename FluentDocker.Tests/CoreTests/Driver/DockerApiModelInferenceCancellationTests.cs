using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Verifies that a caller cancellation occurring while the inference driver reads a
  /// failed response's error body is propagated (as <see cref="OperationCanceledException"/>),
  /// not swallowed by the error-body reader and reported as a generic HTTP failure.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiModelInferenceCancellationTests
  {
    private static DriverContext Ctx => new("docker");

    [Fact]
    public async Task ListEngineModels_CancellationDuringErrorRead_Propagates()
    {
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var connection = new ThrowingErrorConnection();
      var driver = new DockerApiModelInferenceDriver(connection, ModelRunnerEndpoint.HostTcp());

      await Assert.ThrowsAnyAsync<OperationCanceledException>(
          () => driver.ListEngineModelsAsync(Ctx, cts.Token));
    }

    /// <summary>Returns a non-success response whose body read throws OCE (simulating cancellation).</summary>
    private sealed class ThrowingErrorConnection : IModelApiConnection
    {
      public Uri BaseAddress => new("http://localhost:12434");
      public TimeSpan? StreamReadIdleTimeout => null;

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ThrowingContent() });

      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ThrowingContent() });

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ThrowingContent() });

      public Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default) =>
          throw new NotSupportedException();

      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>An <see cref="HttpContent"/> whose read always throws <see cref="OperationCanceledException"/>.</summary>
    private sealed class ThrowingContent : HttpContent
    {
      protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context) =>
          throw new OperationCanceledException();

      protected override bool TryComputeLength(out long length)
      {
        length = 0;
        return false;
      }
    }
  }
}
