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
  /// Regression tests for specific GitHub issues.
  /// Ported from V2 IssuesTests.cs
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "Regression")]
  [Collection("DockerDriver")]
  public class RegressionTests : DockerDriverTestBase
  {
    /// <summary>
    /// Gets the compose driver.
    /// </summary>
    protected IComposeDriver ComposeDriver => Kernel.SysCtl<IComposeDriver>(DriverId);

    #region Issue 85 - Network inspection

    /// <summary>
    /// Issue 85: Network configuration should be accessible via container inspection.
    /// </summary>
    [Fact]
    public async Task Issue85_ContainerNetworks_ShouldBeAccessible()
    {
      var projectName = UniqueName("issue85");
      var composeFile = GetResourcePath("ComposeTests/MongoDbAndNetwork/docker-compose.yml");

      try
      {
        // Arrange
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(upResult.Success, $"Compose up failed: {upResult.Error}");

        // Wait for container to start
        await Task.Delay(3000, cancellationToken: TestContext.Current.CancellationToken);

        // Get containers in project
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count > 0);

        // Act - Get container details
        var containerService = listResult.Data.First();
        var containerId = containerService.ContainerId;

        if (!string.IsNullOrEmpty(containerId))
        {
          var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

          // Assert
          Assert.True(inspect.Success);
          Assert.NotNull(inspect.Data.NetworkSettings);
          Assert.NotNull(inspect.Data.NetworkSettings.Networks);
          Assert.True(inspect.Data.NetworkSettings.Networks.Count > 0);
        }
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        }, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region Issue 92 - Wait for specific host

    /// <summary>
    /// Issue 92: WaitForPort should work with specific host address.
    /// </summary>
    [Fact]
    public async Task Issue92_WaitForPort_WithSpecificHost_ShouldWork()
    {
      string? containerId = null;
      try
      {
        // Arrange
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = NginxImage,
          Name = UniqueName("issue92"),
          PortBindings = new Dictionary<string, string>
          {
            ["80/tcp"] = "0" // Random port
          },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get the mapped port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);

        var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
        Assert.NotNull(portBinding);
        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait for port on 127.0.0.1 specifically
        var isReady = await WaitForHttpAsync($"http://127.0.0.1:{hostPort}/", TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(isReady, "Should be able to reach nginx on 127.0.0.1");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Issue 94 - Service and instance parsing

    /// <summary>
    /// Issue 94: Compose service names should be properly parsed.
    /// </summary>
    [Fact]
    public async Task Issue94_ComposeServices_ShouldHaveProperNames()
    {
      var projectName = UniqueName("issue94");
      var composeFile = GetResourcePath("ComposeTests/KafkaAndZookeeper/docker-compose.yaml");

      try
      {
        // Arrange
        var upResult = await ComposeDriver.UpAsync(Context, new ComposeUpConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          Detached = true,
          RemoveOrphans = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(upResult.Success);

        // Wait for services to start
        await Task.Delay(5000, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var listResult = await ComposeDriver.ListAsync(Context, new ComposeListConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          All = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(listResult.Success);
        Assert.True(listResult.Data.Count >= 2);

        // Verify service names are present
        var serviceNames = listResult.Data.Select(s => s.Name?.ToLower()).ToList();
        Assert.Contains(serviceNames, n => n != null && n.Contains("kafka"));
        Assert.Contains(serviceNames, n => n != null && n.Contains("zookeeper"));
      }
      finally
      {
        await ComposeDriver.DownAsync(Context, new ComposeDownConfig
        {
          ComposeFiles = new List<string> { composeFile },
          ProjectName = projectName,
          RemoveVolumes = true
        }, cancellationToken: TestContext.Current.CancellationToken);
      }
    }

    #endregion

    #region Container name collision

    /// <summary>
    /// Test that container name collisions are handled properly.
    /// </summary>
    [Fact]
    public async Task ContainerNameCollision_ShouldBeHandledGracefully()
    {
      var containerName = UniqueName("collision");
      string? container1Id = null;

      try
      {
        // Arrange - Create first container
        var result1 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Name = containerName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result1.Success);
        container1Id = result1.Data.Id;

        // Act - Try to create second container with same name
        var result2 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Name = containerName,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Second create should fail (name in use)
        Assert.False(result2.Success);
      }
      finally
      {
        if (container1Id != null)
          await RemoveContainerAsync(container1Id);
      }
    }

    #endregion

    #region Volume persistence

    /// <summary>
    /// Test that volumes persist data correctly between container restarts.
    /// </summary>
    [Fact]
    public async Task VolumePersistence_DataSurvivesContainerRestart()
    {
      string? containerId = null;
      var volumeName = UniqueName("persist");
      var testData = $"test-data-{Guid.NewGuid()}";

      try
      {
        // Arrange - Create volume
        await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName }, cancellationToken: TestContext.Current.CancellationToken);

        // Write data in first container
        var result1 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/data"
          },
          Command = new[] { "sh", "-c", $"echo \"{testData}\" > /data/test.txt" },
          Detach = false
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result1.Success);

        // Remove first container
        if (result1.Data?.Id != null)
          await RemoveContainerAsync(result1.Data.Id);

        // Act - Read data in second container
        var result2 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Volumes = new Dictionary<string, string>
          {
            [volumeName] = "/data"
          },
          Command = new[] { "cat", "/data/test.txt" },
          Detach = false
        }, cancellationToken: TestContext.Current.CancellationToken);
        containerId = result2.Data?.Id;

        // Get logs to see output
        await Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken);
        if (containerId != null)
        {
          var logs = await ContainerDriver.GetLogsAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

          // Assert
          Assert.True(result2.Success);
          Assert.Contains(testData, logs.Data);
        }
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
        await RemoveVolumeAsync(volumeName);
      }
    }

    #endregion

    #region Network isolation

    /// <summary>
    /// Test that containers on different networks cannot communicate.
    /// </summary>
    [Fact]
    public async Task NetworkIsolation_ContainersOnDifferentNetworks_CannotCommunicate()
    {
      string? container1Id = null;
      string? container2Id = null;
      string? network1Id = null;
      string? network2Id = null;

      try
      {
        // Arrange - Create two separate networks
        var network1Name = UniqueName("net1");
        var network2Name = UniqueName("net2");

        network1Id = await CreateNetworkAsync(network1Name, new NetworkCreateConfig
        {
          Name = network1Name,
          Driver = "bridge"
        });

        network2Id = await CreateNetworkAsync(network2Name, new NetworkCreateConfig
        {
          Name = network2Name,
          Driver = "bridge"
        });

        // Create containers on different networks
        var result1 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          NetworkMode = network1Name,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result1.Success);
        container1Id = result1.Data.Id;

        var result2 = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          NetworkMode = network2Name,
          Command = new[] { "sleep", "60" },
          Detach = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result2.Success);
        container2Id = result2.Data.Id;

        // Get container1's IP
        var inspect1 = await ContainerDriver.InspectAsync(Context, container1Id, cancellationToken: TestContext.Current.CancellationToken);
        var container1Ip = inspect1.Data.NetworkSettings?.Networks?[network1Name]?.IPAddress;
        Assert.NotNull(container1Ip);

        // Act - Try to ping from container2 to container1 (should fail)
        var pingResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
        {
          Command = new[] { "ping", "-c", "1", "-W", "2", container1Ip }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Ping should fail (different networks)
        Assert.True(pingResult.Success == false || pingResult.Data.ExitCode != 0);
      }
      finally
      {
        if (container1Id != null)
          await RemoveContainerAsync(container1Id);
        if (container2Id != null)
          await RemoveContainerAsync(container2Id);
        if (network1Id != null)
          await RemoveNetworkAsync(network1Id);
        if (network2Id != null)
          await RemoveNetworkAsync(network2Id);
      }
    }

    #endregion

    #region Helper Methods

    private string GetResourcePath(string relativePath)
    {
      var basePath = Path.GetDirectoryName(typeof(RegressionTests).Assembly.Location);
      var resourcePath = Path.Combine(basePath ?? "", "Resources", relativePath);

      if (!File.Exists(resourcePath))
      {
        resourcePath = Path.Combine(Directory.GetCurrentDirectory(), "Resources", relativePath);
      }

      return resourcePath;
    }

    private async Task<bool> WaitForHttpAsync(string url, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;
      using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

      while (DateTime.UtcNow < endTime)
      {
        try
        {
          var response = await client.GetAsync(url);
          if (response.IsSuccessStatusCode)
            return true;
        }
        catch
        {
          // Retry
        }

        await Task.Delay(500);
      }

      return false;
    }

    #endregion
  }
}
