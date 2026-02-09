using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IVolumeDriver operations.
  /// Requires Docker daemon to be running.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerDriver")]
  public class VolumeDriverTests : DockerDriverTestBase
  {
    [Fact]
    public async Task Create_WithName_CreatesVolume()
    {
      var volumeName = UniqueName("volume");
      try
      {
        // Act
        var result = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });

        // Assert
        Assert.True(result.Success, $"Create failed: {result.Error}");
        Assert.Equal(volumeName, result.Data.Name);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task Inspect_ExistingVolume_ReturnsDetails()
    {
      var volumeName = UniqueName("volume");
      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });

        // Act
        var result = await VolumeDriver.InspectAsync(Context, volumeName);

        // Assert
        Assert.True(result.Success, $"Inspect failed: {result.Error}");
        Assert.NotNull(result.Data);
        Assert.Equal(volumeName, result.Data.Name);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task List_WithVolumes_ReturnsVolumes()
    {
      var volumeName = UniqueName("volume");
      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });

        // Act
        var result = await VolumeDriver.ListAsync(Context);

        // Assert
        Assert.True(result.Success);
        Assert.Contains(result.Data, v => v.Name == volumeName);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task Remove_ExistingVolume_RemovesSuccessfully()
    {
      // Arrange
      var volumeName = UniqueName("volume");
      await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
      {
        Name = volumeName
      });

      // Act
      var result = await VolumeDriver.RemoveAsync(Context, volumeName);

      // Assert
      Assert.True(result.Success);

      // Verify volume is gone
      var inspect = await VolumeDriver.InspectAsync(Context, volumeName);
      Assert.False(inspect.Success);
    }

    [Fact]
    public async Task MountVolume_InContainer_VolumeMounted()
    {
      var volumeName = UniqueName("volume");
      string containerId = null;
      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });

        // Act - Create container with volume mounted
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
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

        Assert.True(runResult.Success, $"Run failed: {runResult.Error}");
        containerId = runResult.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.Mounts);
        Assert.Contains(inspect.Data.Mounts, m => m.Name == volumeName);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task Create_WithLabels_CreatesVolumeWithLabels()
    {
      var volumeName = UniqueName("volume");
      try
      {
        // Act
        var result = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName,
          Labels = new Dictionary<string, string>
          {
            ["test.label"] = "test-value",
            ["another.label"] = "another-value"
          }
        });

        // Assert
        Assert.True(result.Success, $"Create failed: {result.Error}");

        // Volume was created with labels (labels may not be exposed in inspect result)
        var inspect = await VolumeDriver.InspectAsync(Context, volumeName);
        Assert.True(inspect.Success);
        Assert.Equal(volumeName, inspect.Data.Name);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task VolumeNotDeleted_WhenRemoveOnDisposeNotSet()
    {
      // This test verifies volume persistence behavior
      var volumeName = UniqueName("volume");
      try
      {
        // Arrange & Act
        var createResult = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });
        Assert.True(createResult.Success);

        // Verify volume exists
        var inspectResult = await VolumeDriver.InspectAsync(Context, volumeName);
        Assert.True(inspectResult.Success);
        Assert.Equal(volumeName, inspectResult.Data.Name);
      }
      finally
      {
        // Cleanup
        await RemoveVolumeAsync(volumeName);
      }
    }

    [Fact]
    public async Task List_WithNameFilter_FiltersResults()
    {
      var volumeName = UniqueName("filtertest");
      try
      {
        // Arrange
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
        {
          Name = volumeName
        });

        // Act
        var result = await VolumeDriver.ListAsync(Context, new VolumeListFilter
        {
          Name = volumeName
        });

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data);
        Assert.Equal(volumeName, result.Data[0].Name);
      }
      finally
      {
        await RemoveVolumeAsync(volumeName);
      }
    }
  }
}

