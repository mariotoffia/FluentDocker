using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Container driver operations tests: exec, top, rename, kill, stats, copy, networking.
  /// </summary>
  [Trait("Category", "Integration")]
  public partial class ContainerDriverTests
  {
    [Fact]
    public async Task Exec_InRunningContainer_ExecutesCommand()
    {
      string containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "echo", "hello" }
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("hello", result.Data.StdOut);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Top_RunningContainer_ReturnsProcesses()
    {
      string containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.TopAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Processes.Count > 0, "Should have at least one process");
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Rename_ExistingContainer_RenamesSuccessfully()
    {
      string containerId = null;
      var newName = UniqueName("renamed");
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.RenameAsync(Context, containerId, newName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(newName, inspect.Data.Name);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Kill_RunningContainer_KillsContainer()
    {
      string containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.KillAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        // Container should be stopped
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(inspect.Data.State.Running);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task Stats_RunningContainer_ReturnsResourceUsage()
    {
      string containerId = null;
      try
      {
        // Arrange
        containerId = await RunContainerAsync(NginxImage);

        // Act
        var result = await ContainerDriver.StatsAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Stats failed: {result.Error}");
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data.Name);

        // Memory limit should be greater than 0 (unless unlimited)
        Assert.True(result.Data.MemoryLimit >= 0);

        // CPU percentage should be >= 0
        Assert.True(result.Data.CpuPercent >= 0);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
      }
    }

    [Fact]
    public async Task CopyTo_FileToContainer_Succeeds()
    {
      string containerId = null;
      var tempFile = System.IO.Path.GetTempFileName();
      try
      {
        // Arrange - use alpine with sleep command
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });
        System.IO.File.WriteAllText(tempFile, "Hello from host!");

        // Act
        var result = await ContainerDriver.CopyToAsync(Context, containerId, tempFile, "/tmp/testfile.txt", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"CopyTo failed: {result.Error}");

        // Wait a bit for file system sync
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        // Verify file exists in container using ls first
        var lsResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "ls", "-la", "/tmp/" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(lsResult.Success, $"ls failed: {lsResult.Error}");

        // Verify file content
        var execResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "cat", "/tmp/testfile.txt" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(execResult.Success, $"cat failed: {execResult.Error}. ls output: {lsResult.Data.StdOut}");
        Assert.Contains("Hello from host!", execResult.Data.StdOut);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (System.IO.File.Exists(tempFile))
          System.IO.File.Delete(tempFile);
      }
    }

    [Fact]
    public async Task CopyFrom_FileFromContainer_Succeeds()
    {
      string containerId = null;
      var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
      try
      {
        // Arrange
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });
        System.IO.Directory.CreateDirectory(tempDir);

        // Create a file in the container
        var execResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "/bin/sh", "-c", "echo 'Hello from container!' > /tmp/containerfile.txt" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(execResult.Success, $"Exec failed: {execResult.Error}");

        // Wait a bit for file system sync
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        // Verify file was created
        var verifyResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "cat", "/tmp/containerfile.txt" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(verifyResult.Success, $"File verification failed: {verifyResult.Error}");
        Assert.Contains("Hello from container!", verifyResult.Data.StdOut);

        // Act
        var result = await ContainerDriver.CopyFromAsync(Context, containerId, "/tmp/containerfile.txt", tempDir, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"CopyFrom failed: {result.Error}");

        // Verify file was copied to host
        var copiedFile = System.IO.Path.Combine(tempDir, "containerfile.txt");
        Assert.True(System.IO.File.Exists(copiedFile), $"File not found at: {copiedFile}");
        var content = System.IO.File.ReadAllText(copiedFile);
        Assert.Contains("Hello from container!", content);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (System.IO.Directory.Exists(tempDir))
          System.IO.Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public async Task CopyTo_DirectoryToContainer_Succeeds()
    {
      string containerId = null;
      var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
      try
      {
        // Arrange
        containerId = await RunContainerAsync(TestImage, new ContainerCreateConfig
        {
          Command = new[] { "sleep", "60" }
        });
        System.IO.Directory.CreateDirectory(tempDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "file1.txt"), "Content 1");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "file2.txt"), "Content 2");

        // Act
        var result = await ContainerDriver.CopyToAsync(Context, containerId, tempDir, "/tmp/testdir", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"CopyTo directory failed: {result.Error}");

        // Verify files exist in container
        var execResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "ls", "/tmp/testdir" }
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(execResult.Success);
        Assert.Contains("file1.txt", execResult.Data.StdOut);
        Assert.Contains("file2.txt", execResult.Data.StdOut);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        if (System.IO.Directory.Exists(tempDir))
          System.IO.Directory.Delete(tempDir, true);
      }
    }

    [Fact]
    public async Task Run_WithStaticIPv4_AssignsIPAddress()
    {
      string containerId = null;
      string networkId = null;
      var networkName = UniqueName("ipv4-test-net");
      // Use a less common subnet to avoid conflicts with existing Docker networks
      var staticIp = "10.199.0.100";
      try
      {
        // Arrange - Create a network with a specific subnet
        var networkResult = await NetworkDriver.CreateAsync(Context, new Drivers.NetworkCreateConfig
        {
          Name = networkName,
          Driver = "bridge",
          Subnet = "10.199.0.0/16",
          Gateway = "10.199.0.1"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(networkResult.Success, $"Network create failed: {networkResult.Error}");
        networkId = networkResult.Data.Id;

        // Act - Run container with static IP
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          Detach = true,
          NetworkMode = networkName,
          Ipv4Address = staticIp
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success, $"Run with IPv4 failed: {result.Error}");
        containerId = result.Data.Id;

        // Verify the IP address was assigned
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(inspect.Success);

        var networks = inspect.Data.NetworkSettings?.Networks;
        Assert.NotNull(networks);
        Assert.True(networks.ContainsKey(networkName), $"Container not connected to network {networkName}");

        var networkConfig = networks[networkName];
        Assert.Equal(staticIp, networkConfig.IPAddress);
      }
      finally
      {
        await RemoveContainerAsync(containerId);
        await RemoveNetworkAsync(networkId);
      }
    }
  }
}
