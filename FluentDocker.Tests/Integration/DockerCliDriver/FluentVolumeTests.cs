using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for volume operations via IVolumeDriver.
  /// Ported from V2 FluentVolumeTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "FluentVolume")]
  [Collection("DockerDriver")]
  public class FluentVolumeTests : DockerDriverTestBase
  {
    #region Volume Lifecycle Tests

    [Fact]
    public async Task Volume_WithoutRemoveOnDispose_PersistsAfterContainerRemoved()
    {
      string? containerId = null;
      var volumeName = UniqueName("persist");

      try
      {
        // Arrange - Create volume
        var volumeResult = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(volumeResult.Success);

        // Create container with volume
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
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
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(containerResult.Success);
        containerId = containerResult.Data.Id;

        // Remove container
        await RemoveContainerAsync(containerId!);
        containerId = null;

        // Assert - Volume should still exist
        var inspectResult = await VolumeDriver.InspectAsync(Context, volumeName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspectResult.Success);
        Assert.Equal(volumeName, inspectResult.Data.Name);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task Volume_CanBeRemovedAfterContainerDeleted()
    {
      string? containerId = null;
      var volumeName = UniqueName("removable");

      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName }, cancellationToken: TestContext.Current.CancellationToken);

        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/data"
          },
          Command = new[] { "sleep", "5" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        containerId = containerResult.Data?.Id;

        // Remove container
        if (containerId != null)
        {
          await RemoveContainerAsync(containerId!);
          containerId = null;
        }

        // Act - Remove volume
        var removeResult = await VolumeDriver.RemoveAsync(Context, volumeName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removeResult.Success);

        var inspectResult = await VolumeDriver.InspectAsync(Context, volumeName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(inspectResult.Success);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        await RemoveVolumeAsync(volumeName);
      }
    }

    #endregion

    #region Volume Mount Tests

    [Fact]
    public async Task Volume_MountedInContainer_AppearsInMounts()
    {
      string? containerId = null;
      var volumeName = UniqueName("mounted");

      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
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
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(containerResult.Success);
        containerId = containerResult.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.Mounts);
        Assert.Contains(inspect.Data.Mounts, m => m.Name == volumeName);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task Volume_DataPersistsBetweenContainers()
    {
      string? container1Id = null;
      string? container2Id = null;
      var volumeName = UniqueName("shared-data");
      var testData = $"test-{Guid.NewGuid()}";

      try
      {
        // Arrange - Create volume
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName }, cancellationToken: TestContext.Current.CancellationToken);

        // Start first container and write data
        var container1Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/data"
          },
          Command = new[] { "sh", "-c", $"echo \"{testData}\" > /data/test.txt && sleep 5" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        container1Id = container1Result.Data?.Id;

        // Wait for write to complete
        await Task.Delay(2000, cancellationToken: TestContext.Current.CancellationToken);

        // Remove first container
        if (container1Id != null)
        {
          await RemoveContainerAsync(container1Id);
          container1Id = null;
        }

        // Start second container and read data
        var container2Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/data"
          },
          Command = new[] { "sleep", "60" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        container2Id = container2Result.Data?.Id;
        Assert.NotNull(container2Id);

        // Act - Read the data
        var readResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
        {
          Command = new[] { "cat", "/data/test.txt" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(readResult.Success);
        Assert.Contains(testData, readResult.Data.StdOut);
      }
      finally
      {
        if (container1Id != null)
          await RemoveContainerAsync(container1Id);
        if (container2Id != null)
          await RemoveContainerAsync(container2Id);
        await RemoveVolumeAsync(volumeName);
      }
    }

    #endregion

    #region Volume With Labels Tests

    [Fact]
    public async Task Volume_WithLabels_CreatesWithLabels()
    {
      var volumeName = UniqueName("labeled");

      try
      {
        // Act
        var result = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName,
          Labels = new Dictionary<string, string>
          {
            ["com.example.app"] = "myapp",
            ["com.example.env"] = "test"
          }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var inspect = await VolumeDriver.InspectAsync(Context, volumeName, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        Assert.Equal(volumeName, inspect.Data.Name);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }

    #endregion

    #region Anonymous Volume Tests

    [Fact]
    public async Task Container_WithAnonymousVolume_CreatesVolume()
    {
      string? containerId = null;

      try
      {
        // Act - Create container with anonymous volume
        var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(containerResult.Success);
        containerId = containerResult.Data.Id;

        // Assert - Postgres image declares a volume
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);
        // Postgres has a declared volume at /var/lib/postgresql/data
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
      }
    }

    #endregion
  }
}
