using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Base class for DockerCliDriver integration tests.
  /// Provides common setup, teardown, and utility methods.
  /// </summary>
  public abstract class DockerDriverTestBase : IAsyncLifetime
  {
    protected FluentDockerKernel Kernel { get; private set; }
    protected string DriverId => "docker";
    protected DriverContext Context => new DriverContext(DriverId);

    // Standard test image - small and fast
    protected const string TestImage = "alpine:latest";
    protected const string PostgresImage = "postgres:13-alpine";
    protected const string NginxImage = "nginx:alpine";

    /// <summary>Label applied to all test-created containers for easy cleanup.</summary>
    protected const string TestLabelKey = "com.fluentdocker.test";
    protected const string TestLabelValue = "integration";

    public async Task InitializeAsync()
    {
      // Ensure Linux daemon mode on Windows
      if (FdOs.IsWindows())
      {
        // Will attempt to switch to Linux mode
      }

      Kernel = await FluentDockerKernel.Create()
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();
    }

    public Task DisposeAsync()
    {
      Kernel?.Dispose();
      return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the container driver.
    /// </summary>
    protected IContainerDriver ContainerDriver => Kernel.SysCtl<IContainerDriver>(DriverId);

    /// <summary>
    /// Gets the network driver.
    /// </summary>
    protected INetworkDriver NetworkDriver => Kernel.SysCtl<INetworkDriver>(DriverId);

    /// <summary>
    /// Gets the volume driver.
    /// </summary>
    protected IVolumeDriver VolumeDriver => Kernel.SysCtl<IVolumeDriver>(DriverId);

    /// <summary>
    /// Gets the image driver.
    /// </summary>
    protected IImageDriver ImageDriver => Kernel.SysCtl<IImageDriver>(DriverId);

    /// <summary>
    /// Gets the system driver.
    /// </summary>
    protected ISystemDriver SystemDriver => Kernel.SysCtl<ISystemDriver>(DriverId);

    /// <summary>
    /// Ensures an image is available locally.
    /// </summary>
    protected async Task EnsureImageAsync(string image)
    {
      var parts = image.Split(':');
      var name = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "latest";

      await ImageDriver.PullAsync(Context, name, tag);
    }

    /// <summary>
    /// Creates and runs a container, returning its ID.
    /// </summary>
    protected async Task<string> RunContainerAsync(string image, ContainerCreateConfig config = null)
    {
      config ??= new ContainerCreateConfig();
      config.Image = image;
      config.Detach = true;
      config.Labels ??= new Dictionary<string, string>();
      config.Labels[TestLabelKey] = TestLabelValue;

      var result = await ContainerDriver.RunAsync(Context, config);
      Assert.True(result.Success, $"Failed to run container: {result.Error}");
      return result.Data.Id;
    }

    /// <summary>
    /// Removes a container forcefully.
    /// </summary>
    protected async Task RemoveContainerAsync(string containerId)
    {
      if (!string.IsNullOrEmpty(containerId))
      {
        await ContainerDriver.RemoveAsync(Context, containerId, force: true, removeVolumes: true);
      }
    }

    /// <summary>
    /// Creates a network and returns its ID.
    /// </summary>
    protected async Task<string> CreateNetworkAsync(string name, NetworkCreateConfig config = null)
    {
      config ??= new NetworkCreateConfig();
      config.Name = name;

      var result = await NetworkDriver.CreateAsync(Context, config);
      Assert.True(result.Success, $"Failed to create network: {result.Error}");
      return result.Data.Id;
    }

    /// <summary>
    /// Removes a network.
    /// </summary>
    protected async Task RemoveNetworkAsync(string networkId)
    {
      if (!string.IsNullOrEmpty(networkId))
      {
        await NetworkDriver.RemoveAsync(Context, networkId);
      }
    }

    /// <summary>
    /// Creates a volume and returns its name.
    /// </summary>
    protected async Task<string> CreateVolumeAsync(string name = null)
    {
      var config = new VolumeCreateConfig { Name = name ?? $"test-vol-{Guid.NewGuid():N}" };
      var result = await VolumeDriver.CreateAsync(Context, config);
      Assert.True(result.Success, $"Failed to create volume: {result.Error}");
      return result.Data.Name;
    }

    /// <summary>
    /// Removes a volume.
    /// </summary>
    protected async Task RemoveVolumeAsync(string volumeName)
    {
      if (!string.IsNullOrEmpty(volumeName))
      {
        await VolumeDriver.RemoveAsync(Context, volumeName, force: true);
      }
    }

    /// <summary>
    /// Generates a unique name for test resources.
    /// </summary>
    protected string UniqueName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}".Substring(0, 20);
  }
}

