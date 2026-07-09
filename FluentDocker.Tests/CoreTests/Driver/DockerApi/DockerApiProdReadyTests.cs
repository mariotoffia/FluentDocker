using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiProdReadyTests
  {
    private static DriverContext Ctx => new("docker-api-prod-ready-tests");

    [Fact]
    public async Task PushAsync_ProgressWithoutDigest_ReturnsPushFailed()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/images/",
          "{\"status\":\"Pushing\",\"id\":\"layer1\"}\n" +
          "{\"status\":\"Pushed\",\"id\":\"layer1\"}\n");
      var driver = new DockerApiImageDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.PushAsync(Ctx, "repo/app:latest", null!,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("terminal digest", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PushAsync_AuxDigest_ReturnsSuccess()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/images/",
          "{\"status\":\"Pushing\",\"id\":\"layer1\"}\n" +
          "{\"aux\":{\"Digest\":\"sha256:abc123\"}}\n");
      var driver = new DockerApiImageDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.PushAsync(Ctx, "repo/app:latest", null!,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task PushAsync_StatusDigestLine_ReturnsSuccess()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/images/",
          "{\"status\":\"Pushing\",\"id\":\"layer1\"}\n" +
          "{\"status\":\"latest: digest: sha256:abc123 size: 123\"}\n");
      var driver = new DockerApiImageDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.PushAsync(Ctx, "repo/app:latest", null!,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ReadNdjsonPostStream_MidReadIOException_ReportsStreamInterrupted()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStreamReadThrows("/events",
          Encoding.UTF8.GetBytes("{\"status\":\"ok\"}\n"),
          new IOException("connection reset by peer"));
      var driver = new ExposedDriver(mock);
      driver.Initialize(Ctx);

      var error = await Assert.ThrowsAsync<DriverException>(() =>
          driver.ReadPostNdjsonToEndAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.Api.StreamInterrupted, error.ErrorCode);
      Assert.Contains("mid-stream", error.Message, StringComparison.OrdinalIgnoreCase);
      // A mid-stream transport interruption is retryable (a re-established stream may succeed),
      // matching the prior Api.ConnectionFailed semantics — must stay transient.
      Assert.True(error.IsTransient);
    }

    [Fact]
    public async Task NetworkCreate_SyntheticConnectionFailure_MapsToApiConnectionFailed()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/networks/create", 599, "{\"message\":\"cannot connect\"}");
      var driver = new DockerApiNetworkDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.CreateAsync(Ctx, new NetworkCreateConfig { Name = "net" },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task VolumePrune_SyntheticTimeout_MapsToGeneralTimeout()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/volumes/prune", 408, "{\"message\":\"timed out\"}");
      var driver = new DockerApiVolumeDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.PruneAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.Timeout, result.ErrorCode);
    }

    [Fact]
    public async Task ServiceList_JobModes_ReportsDistinctModeLabels()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/services", 200,
          "[{\"ID\":\"job1\",\"Spec\":{\"Name\":\"batch\"," +
          "\"TaskTemplate\":{\"ContainerSpec\":{\"Image\":\"app:latest\"}}," +
          "\"Mode\":{\"ReplicatedJob\":{\"TotalCompletions\":3,\"MaxConcurrent\":1}}}}," +
          "{\"ID\":\"job2\",\"Spec\":{\"Name\":\"fanout\"," +
          "\"TaskTemplate\":{\"ContainerSpec\":{\"Image\":\"app:latest\"}}," +
          "\"Mode\":{\"GlobalJob\":{}}}}]");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ListAsync(Ctx, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotNull(result.Data);
      Assert.Equal("replicated-job", result.Data[0].Mode);
      Assert.Equal("3", result.Data[0].Replicas);
      Assert.Equal("global-job", result.Data[1].Mode);
    }

    [Fact]
    public async Task ServiceInspect_ReplicatedJob_UsesTotalCompletionsAsReplicas()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/services/job1", 200,
          "{\"ID\":\"job1\",\"Version\":{\"Index\":7},\"Spec\":{\"Name\":\"batch\"," +
          "\"TaskTemplate\":{\"ContainerSpec\":{\"Image\":\"app:latest\"}}," +
          "\"Mode\":{\"ReplicatedJob\":{\"TotalCompletions\":5,\"MaxConcurrent\":2}}}}");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.InspectAsync(Ctx, "job1",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotNull(result.Data);
      Assert.Equal("replicated-job", result.Data.Mode);
      Assert.Equal(5, result.Data.Replicas);
    }

    private sealed class ExposedDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection)
    {
      public async Task ReadPostNdjsonToEndAsync(CancellationToken cancellationToken)
      {
        using var content = new StringContent(string.Empty);
        await foreach (var line in ReadNdjsonFromPostStreamAsync(
            "/events", content, cancellationToken).ConfigureAwait(false))
        {
          _ = line;
        }
      }
    }
  }
}
