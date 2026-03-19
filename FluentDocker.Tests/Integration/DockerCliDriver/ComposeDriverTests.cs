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
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // List services
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
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
      }, TestContext.Current.CancellationToken);
      Assert.True(upResult.Success);

      // Act
      var downResult = await ComposeDriver.DownAsync(Context, new ComposeDownConfig
      {
        ComposeFiles = new List<string> { composeFile },
        ProjectName = projectName,
        RemoveVolumes = true
      }, TestContext.Current.CancellationToken);

      // Assert
      Assert.True(downResult.Success);

      // Verify services are gone
      var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
      {
        ComposeFiles = new List<string> { composeFile },
        ProjectName = projectName,
        All = true
      }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Wait for services to start
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        // Act - Pause
        var pauseResult = await ComposeDriver.PauseAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(pauseResult.Success, $"Pause failed: {pauseResult.Error}");

        // Verify paused
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);

        // Act - Unpause
        var unpauseResult = await ComposeDriver.UnpauseAsync(Context, new ComposeFileConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        }, TestContext.Current.CancellationToken);
        Assert.True(unpauseResult.Success, $"Unpause failed: {unpauseResult.Error}");
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
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
        }, TestContext.Current.CancellationToken);
        Assert.True(upResult.Success);

        // Wait for some logs
        await Task.Delay(5000, TestContext.Current.CancellationToken);

        // Act
        var logsResult = await ComposeDriver.GetLogsAsync(Context, new ComposeLogsConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Tail = 50
        }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
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
      }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
        Assert.True(upResult.Success);

        // Wait for service to be ready
        await Task.Delay(10000, TestContext.Current.CancellationToken);

        // Act
        var execResult = await ComposeDriver.ExecuteAsync(Context, new ComposeExecConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Service = "rabbitmq",
          Command = new[] { "rabbitmqctl", "status" },
          Tty = false
        }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
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
        }, TestContext.Current.CancellationToken);
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Verify initial state: exactly 1 worker instance
        var initialList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);
        Assert.True(initialList.Success);
        Assert.Single(initialList.Data);

        // Act — scale worker to 3 instances
        var scaleUpResult = await ComposeDriver.ScaleAsync(Context, new ComposeScaleConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Scale = new Dictionary<string, int> { { "worker", 3 } }
        }, TestContext.Current.CancellationToken);
        Assert.True(scaleUpResult.Success, $"Scale up failed: {scaleUpResult.Error}");

        // Assert — should now have 3 worker instances
        var scaledList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);
        Assert.True(scaledList.Success);
        Assert.Equal(3, scaledList.Data.Count);

        // Act — scale back down to 1
        var scaleDownResult = await ComposeDriver.ScaleAsync(Context, new ComposeScaleConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Scale = new Dictionary<string, int> { { "worker", 1 } }
        }, TestContext.Current.CancellationToken);
        Assert.True(scaleDownResult.Success, $"Scale down failed: {scaleDownResult.Error}");

        // Assert — should be back to 1 instance
        var finalList = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        }, TestContext.Current.CancellationToken);
        Assert.True(finalList.Success);
        Assert.Single(finalList.Data);
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        }, TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region Helper Methods

    private static string GetResourcePath(string relativePath)
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

