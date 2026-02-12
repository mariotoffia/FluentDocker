using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for wait conditions.
  /// Ported from V2 WaitTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "WaitCondition")]
  [Collection("DockerDriver")]
  public partial class WaitConditionTests : DockerDriverTestBase
  {
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    #region Wait For Port Tests

    [Fact]
    public async Task WaitForPort_WhenPortOpen_ReturnsQuickly()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start nginx (port 80)
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          PortBindings = new Dictionary<string, string>
          {
            ["80/tcp"] = "0" // Random port
          },
          Detach = true
        });

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get the mapped port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);

        var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
        Assert.NotNull(portBinding);
        Assert.True(portBinding.Length > 0);

        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait for port to be open
        var isOpen = await WaitForPortAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isOpen, $"Port {hostPort} should be open");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task WaitForPort_PostgresPort_OpensWhenReady()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start postgres
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          PortBindings = new Dictionary<string, string>
          {
            ["5432/tcp"] = "0" // Random port
          },
          Detach = true
        });

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get the mapped port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);

        var portBinding = inspect.Data.NetworkSettings?.Ports?["5432/tcp"];
        Assert.NotNull(portBinding);
        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait for port
        var isOpen = await WaitForPortAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isOpen, $"PostgreSQL port {hostPort} should be open");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Wait For Process Tests

    [Fact]
    public async Task WaitForProcess_WhenProcessRunning_Returns()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start postgres
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Detach = true
        });

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Act - Wait for postgres process
        var processFound = await WaitForProcessAsync(containerId, "postgres", TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(processFound, "Postgres process should be running");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Wait For Http Tests

    [Fact]
    public async Task WaitForHttp_WhenServiceReady_ReturnsSuccess()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start nginx
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          PortBindings = new Dictionary<string, string>
          {
            ["80/tcp"] = "0"
          },
          Detach = true
        });

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get the mapped port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
        Assert.NotNull(portBinding);
        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait for HTTP
        var url = $"http://127.0.0.1:{hostPort}/";
        var isReady = await WaitForHttpAsync(url, TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isReady, "Nginx should respond to HTTP requests");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task WaitForHttp_WithCustomValidation_ChecksContent()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start nginx
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          PortBindings = new Dictionary<string, string>
          {
            ["80/tcp"] = "0"
          },
          Detach = true
        });

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get the mapped port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
        Assert.NotNull(portBinding);
        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait for HTTP with content validation
        var url = $"http://127.0.0.1:{hostPort}/";
        var isReady = await WaitForHttpWithContentAsync(url, "nginx", TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isReady, "Nginx page should contain 'nginx'");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Wait For Health Tests

    [Fact]
    public async Task WaitForHealthy_WhenHealthCheckPasses_ReturnsHealthy()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start container with simple healthcheck that succeeds immediately
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage, // alpine
          Command = new[] { "sleep", "120" },
          HealthCheck = new HealthCheckConfig
          {
            Test = new[] { "CMD", "true" }, // Simple command that always succeeds
            Interval = "1s",
            Retries = 3,
            Timeout = "3s",
            StartPeriod = "1s"
          },
          Detach = true
        });

        Assert.True(runResult.Success, $"Container run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        // Act - Wait for healthy (should happen quickly with 'true' command)
        var isHealthy = await WaitForHealthyAsync(containerId, TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isHealthy, "Container should become healthy");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

  }
}
