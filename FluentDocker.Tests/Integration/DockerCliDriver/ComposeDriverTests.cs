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
  /// Integration tests for Docker Compose operations via IComposeDriver.
  /// Ported from V2 FluentDockerComposeTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "Compose")]
  [Collection("DockerDriver")]
  public partial class ComposeDriverTests : DockerDriverTestBase
  {
    private static readonly HttpClient HttpClient = new HttpClient();

    /// <summary>
    /// Gets the compose driver.
    /// </summary>
    protected IComposeDriver ComposeDriver => Kernel.SysCtl<IComposeDriver>(DriverId);

    #region Compose Up/Down Tests

    [Fact]
    public async Task Compose_Up_StartsServices()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/WordPress/docker-compose-test.yml");

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

        // List services
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        });

        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count >= 1, "Should have at least one service");
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

    [Fact]
    public async Task Compose_Down_StopsAndRemovesServices()
    {
      var projectName = UniqueName("compose");
      var composeFile = GetResourcePath("ComposeTests/WordPress/docker-compose-test.yml");

      // Arrange
      var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
      {
        ComposeFiles = new List<string> { composeFile },
        ProjectName = projectName,
        Detached = true,
        RemoveOrphans = true
      });
      Assert.True(upResult.Success);

      // Act
      var downResult = await ComposeDriver.DownAsync(Context, new ComposeDownConfig
      {
        ComposeFiles = new List<string> { composeFile },
        ProjectName = projectName,
        RemoveVolumes = true
      });

      // Assert
      Assert.True(downResult.Success);

      // Verify services are gone
      var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
      {
        ComposeFiles = new List<string> { composeFile },
        ProjectName = projectName,
        All = true
      });

      Assert.True(listResult.Data.Count == 0 || !listResult.Data.Any(s => s.State == "running"));
    }

    #endregion

    #region Compose Pause/Unpause Tests

    [Fact]
    public async Task Compose_PauseAndUnpause_WorksCorrectly()
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
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Wait for services to start
        await Task.Delay(5000);

        // Act - Pause
        var pauseResult = await ComposeDriver.PauseAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        });
        Assert.True(pauseResult.Success, $"Pause failed: {pauseResult.Error}");

        // Verify paused
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        });
        Assert.True(listResult.Success);

        // Act - Unpause
        var unpauseResult = await ComposeDriver.UnpauseAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        });
        Assert.True(unpauseResult.Success, $"Unpause failed: {unpauseResult.Error}");
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

    #region Compose Logs Tests

    [Fact]
    public async Task Compose_GetLogs_ReturnsLogs()
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

        // Wait for some logs
        await Task.Delay(5000);

        // Act
        var logsResult = await ComposeDriver.GetLogsAsync(Context, new ComposeLogsConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Tail = 50
        });

        // Assert
        Assert.True(logsResult.Success);
        Assert.NotNull(logsResult.Data);
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

    #region Compose Config Tests

    [Fact]
    public async Task Compose_Config_ValidatesAndReturnsConfig()
    {
      var composeFile = GetResourcePath("ComposeTests/WordPress/docker-compose-test.yml");

      // Act
      var configResult = await ComposeDriver.ConfigAsync(Context, new ComposeConfigConfig
      {
        ComposeFiles = new List<string> { composeFile }
      });

      // Assert
      Assert.True(configResult.Success, $"Config failed: {configResult.Error}");
      Assert.NotNull(configResult.Data);
      Assert.Contains("wordpress", configResult.Data.ToLower());
    }

    #endregion

    #region Compose Exec Tests

    [Fact]
    public async Task Compose_Exec_RunsCommandInService()
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

        // Wait for service to be ready
        await Task.Delay(10000);

        // Act
        var execResult = await ComposeDriver.ExecuteAsync(Context, new ComposeExecConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Service = "rabbitmq",
          Command = new[] { "rabbitmqctl", "status" },
          Tty = false
        });

        // Assert
        Assert.True(execResult.Success, $"Exec failed: {execResult.Error}");
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

    #region Compose Scale Tests

    [Fact]
    public async Task Compose_Scale_ScalesService()
    {
      var projectName = UniqueName("compose");
      // Use ScaleTest compose (no host port bindings) to allow multiple replicas
      var composeFile = GetResourcePath("ComposeTests/ScaleTest/docker-compose.yml");

      try
      {
        // Arrange — bring up with 1 instance
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        });
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Verify initial state: exactly 1 worker instance
        var initialList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        });
        Assert.True(initialList.Success);
        Assert.Equal(1, initialList.Data.Count);

        // Act — scale worker to 3 instances
        var scaleUpResult = await ComposeDriver.ScaleAsync(Context, new ComposeScaleConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Scale = new Dictionary<string, int> { { "worker", 3 } }
        });
        Assert.True(scaleUpResult.Success, $"Scale up failed: {scaleUpResult.Error}");

        // Assert — should now have 3 worker instances
        var scaledList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        });
        Assert.True(scaledList.Success);
        Assert.Equal(3, scaledList.Data.Count);

        // Act — scale back down to 1
        var scaleDownResult = await ComposeDriver.ScaleAsync(Context, new ComposeScaleConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Scale = new Dictionary<string, int> { { "worker", 1 } }
        });
        Assert.True(scaleDownResult.Success, $"Scale down failed: {scaleDownResult.Error}");

        // Assert — should be back to 1 instance
        var finalList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        });
        Assert.True(finalList.Success);
        Assert.Equal(1, finalList.Data.Count);
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

        // Act — down WITHOUT removing volumes
        var downResult = await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = false
        });
        Assert.True(downResult.Success, $"Down failed: {downResult.Error}");

        // Assert — volumes must survive the down
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
        // Final cleanup — remove volumes
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        });
      }
    }

    #endregion

    #region Helper Methods

    private string GetResourcePath(string relativePath)
    {
      var basePath = Path.GetDirectoryName(typeof(ComposeDriverTests).Assembly.Location);
      var resourcePath = Path.Combine(basePath ?? "", "Resources", relativePath);

      // If not found, try from current directory
      if (!File.Exists(resourcePath))
      {
        resourcePath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", relativePath);
      }

      return resourcePath;
    }

    #endregion
  }
}

