using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IContainerDriver operations.
  /// Requires Docker daemon to be running.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerDriver")]
  public partial class ContainerDriverTests : DockerDriverTestBase
  {
    [Fact]
    public async Task Run_WithoutArguments_CreatesAndStartsContainer()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;
        Assert.NotNull(containerId);
        Assert.Equal(64, containerId.Length);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Remove_ExistingContainer_Succeeds()
    {
      // Arrange
      var containerId = await RunContainerAsync(NginxImage);

      // Act
      var result = await ContainerDriver.RemoveAsync(Context, containerId, force: true, removeVolumes: true, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result.Success);
    }

    [Fact]
    public async Task List_WithRunningContainer_ReturnsContainer()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.ListAsync(Context, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data.Count >= 1);
        Assert.Contains(result.Data, c => c.Id.StartsWith(containerId[..12]) || containerId.StartsWith(c.Id));
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Inspect_RunningContainer_ReturnsDetails()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data.Name);
        Assert.True(result.Data.State.Running);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Stop_RunningContainer_StopsSuccessfully()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.StopAsync(Context, containerId, timeout: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        // Verify it's stopped
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(inspect.Data.State.Running);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Start_StoppedContainer_StartsSuccessfully()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);
        await ContainerDriver.StopAsync(Context, containerId, timeout: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await ContainerDriver.StartAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Data.State.Running);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Pause_RunningContainer_PausesSuccessfully()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.PauseAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Data.State.Paused);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Unpause_PausedContainer_ResumesSuccessfully()
    {
      string? containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);
        await ContainerDriver.PauseAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await ContainerDriver.UnpauseAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(inspect.Data.State.Paused);
        Assert.True(inspect.Data.State.Running);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Run_WithEnvironmentVariables_SetsEnvironment()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = ["sleep", "30"],
          Environment = new Dictionary<string, string>
          {
            ["TEST_VAR"] = "test_value",
            ["ANOTHER_VAR"] = "another_value"
          },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.Contains(inspect.Data.Config.Env, e => e == "TEST_VAR=test_value");
        Assert.Contains(inspect.Data.Config.Env, e => e == "ANOTHER_VAR=another_value");
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task Run_WithExplicitPortMapping_MapsPort()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          PortBindings = new Dictionary<string, string>
          {
            ["80/tcp"] = "8888"
          },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);

        // Check port binding exists
        var ports = inspect.Data.NetworkSettings?.Ports;
        Assert.NotNull(ports);
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

    [Fact]
    public async Task GetLogs_RunningContainer_ReturnsLogs()
    {
      string? containerId = null;
      try
      {
        // Arrange - run container that produces output
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = ["sh", "-c", "echo 'Hello from container'"],
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        containerId = runResult.Data.Id;

        // Wait a bit for container to finish
        await Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await ContainerDriver.GetLogsAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        // Note: Container may have already exited, logs should still be available
      }
      finally
      {
        await RemoveContainerAsync(containerId!);
      }
    }

  }
}

