using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for container operations via IContainerDriver.
  /// Ported from V2 FluentContainerBasicTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "FluentContainer")]
  [Collection("DockerDriver")]
  public partial class FluentContainerTests : DockerDriverTestBase
  {
    #region Basic Container Lifecycle Tests

    [Fact]
    public async Task Create_Container_StartsInStoppedMode()
    {
      string? containerId = null;
      try
      {
        // Arrange & Act - Create container without starting
        var result = await ContainerDriver.CreateAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          }
        });

        Assert.True(result.Success, $"Create failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert - Container should not be running
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.False(inspect.Data.State.Running);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Run_WithEnvironment_SetsEnvironmentVariables()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Detach = true
        });

        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.True(inspect.Data.State.Running);
        Assert.Contains(inspect.Data.Config.Env, e => e == "POSTGRES_PASSWORD=mysecretpassword");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task PauseAndUnpause_Container_WorksCorrectly()
    {
      string? containerId = null;
      try
      {
        // Arrange
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

        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Data.State.Running);

        // Act - Pause
        var pauseResult = await ContainerDriver.PauseAsync(Context, containerId);
        Assert.True(pauseResult.Success);

        // Assert - Paused
        inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Data.State.Paused);

        // Act - Unpause
        var unpauseResult = await ContainerDriver.UnpauseAsync(Context, containerId);
        Assert.True(unpauseResult.Success);

        // Assert - Running again
        inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Data.State.Running);
        Assert.False(inspect.Data.State.Paused);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Port Mapping Tests

    [Fact]
    public async Task Run_WithExplicitPortMapping_MapsPortCorrectly()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          PortBindings = new Dictionary<string, string>
          {
            ["5432/tcp"] = "40001"
          },
          Detach = true
        });

        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.NetworkSettings?.Ports);

        // Verify the port is bound
        var portKey = inspect.Data.NetworkSettings.Ports.Keys
            .FirstOrDefault(k => k.Contains("5432"));
        Assert.NotNull(portKey);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Persistence Tests

    [Fact]
    public async Task Container_WithKeepContainer_RemainsAfterStop()
    {
      string? containerId = null;
      try
      {
        // Arrange & Act
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

        // Stop the container
        var stopResult = await ContainerDriver.StopAsync(Context, containerId, timeout: 5);
        Assert.True(stopResult.Success);

        // Assert - Container should still exist (not auto-removed)
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.False(inspect.Data.State.Running);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Container_WithAutoRemove_RemovedAfterStop()
    {
      // Note: With --rm flag, container is automatically removed when it stops
      var containerName = UniqueName("autorm");

      // Act - Run container with command that exits quickly
      var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
      {
        Image = TestImage,
        Name = containerName,
        Command = new[] { "echo", "hello" },
        AutoRemove = true,
        Detach = false // Wait for completion
      });

      // Assert - Container should be auto-removed after command completes
      await Task.Delay(2000); // Give Docker time to clean up

      var inspect = await ContainerDriver.InspectAsync(Context, containerName);
      // Container should not exist (auto-removed)
      Assert.False(inspect.Success);
    }

    #endregion

    #region Volume Mount Tests

    [Fact]
    public async Task Container_WithVolumeMount_MountsVolume()
    {
      string? containerId = null;
      var volumeName = UniqueName("vol");

      try
      {
        // Arrange - Create volume
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName });

        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/var/lib/postgresql/data"
          },
          Detach = true
        });

        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.Mounts);
        Assert.Contains(inspect.Data.Mounts, m => m.Name == volumeName);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
        await RemoveVolumeAsync(volumeName);
      }
    }

    #endregion

  }
}
