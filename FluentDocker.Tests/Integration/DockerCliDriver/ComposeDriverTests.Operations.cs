using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Compose operations tests: stop/start, kill, build, network, multi-service, volumes.
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "Compose")]
  public partial class ComposeDriverTests
  {
    #region Compose Stop/Start Tests

    [Fact]
    public async Task Compose_StopAndStart_WorksCorrectly()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        // Arrange
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });
        Assert.True(upResult.Success);

        // Wait for service to start
        await Task.Delay(5000);

        // Act - Stop
        var stopResult = await ComposeDriver.StopAsync(Context, new ComposeStopConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Timeout = 10
        });
        Assert.True(stopResult.Success, $"Stop failed: {stopResult.Error}");

        // Act - Start
        var startResult = await ComposeDriver.StartAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        });
        Assert.True(startResult.Success, $"Start failed: {startResult.Error}");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion

    #region Compose Kill Tests

    [Fact]
    public async Task Compose_Kill_KillsServices()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/RabbitMQ/docker-compose.yml");

      try
      {
        // Arrange
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });
        Assert.True(upResult.Success);

        // Wait for service to start
        await Task.Delay(3000);

        // Act
        var killResult = await ComposeDriver.KillAsync(Context, new ComposeKillConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Signal = "SIGKILL"
        });

        // Assert
        Assert.True(killResult.Success);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion

    #region Compose Build Tests

    [Fact]
    public async Task Compose_UpWithBuild_BuildsAndStarts()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("hellotest/docker-compose.yml");

      try
      {
        // Act
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          Build = true,
          RemoveOrphans = true
        });

        // Assert
        Assert.True(upResult.Success, $"Compose up with build failed: {upResult.Error}");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true,
          RemoveImages = "local"
        });
      }
    }

    #endregion

    #region Compose with Network Tests

    [Fact]
    public async Task Compose_WithCustomNetwork_CreatesNetwork()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/MongoDbAndNetwork/docker-compose.yml");

      try
      {
        // Act
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });

        // Assert
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Verify network was created (compose prefixes with project name)
        var networks = await NetworkDriver.ListAsync(Context);
        Assert.True(networks.Success);
        var projectNetwork = networks.Data
            .FirstOrDefault(n => n.Name.Contains(projectName));
        Assert.NotNull(projectNetwork);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion

    #region Kafka/Zookeeper Multi-Service Tests

    [Fact]
    public async Task Compose_KafkaZookeeper_StartsMultipleServices()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/KafkaAndZookeeper/docker-compose.yaml");

      try
      {
        // Act
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });

        // Assert
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Wait for services
        await Task.Delay(5000);

        // List services
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        });

        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count >= 2, "Should have kafka and zookeeper services");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion

    #region Compose Keep Container Tests

    [Fact]
    public async Task Compose_Down_WithoutRemoveVolumes_KeepsVolumes()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/WordPress/docker-compose-test.yml");

      try
      {
        // Arrange
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Wait for volumes to be created
        await Task.Delay(3000);

        // Get volumes before down
        var volumesBefore = await VolumeDriver.ListAsync(Context);
        Assert.True(volumesBefore.Success);
        var projectVolumes = volumesBefore.Data
            .Where(v => v.Name.Contains(projectName)).ToList();
        Assert.True(projectVolumes.Count > 0,
            "WordPress compose should create at least one named volume");

        // Act -- down WITHOUT removing volumes
        var downResult = await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = false
        });
        Assert.True(downResult.Success, $"Down failed: {downResult.Error}");

        // Assert -- volumes must survive the down
        var volumesAfter = await VolumeDriver.ListAsync(Context);
        Assert.True(volumesAfter.Success);
        var keptVolumes = volumesAfter.Data
            .Where(v => v.Name.Contains(projectName)).ToList();
        Assert.True(keptVolumes.Count >= projectVolumes.Count,
            $"Expected volumes to survive down; before={projectVolumes.Count}, " +
            $"after={keptVolumes.Count}");
      }
      finally
      {
        // Final cleanup -- remove volumes
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion
  }
}
