using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Base class for DockerCliDriver integration tests.
  /// Provides common setup, teardown, and utility methods.
  /// </summary>
  public abstract class DockerDriverTestBase : IAsyncLifetime
  {
    protected FluentDockerKernel Kernel { get; private set; } = null!;
    protected static string DriverId => "docker";
    protected static DriverContext Context => new(DriverId);

    // Standard test image - small and fast
    protected const string TestImage = "alpine:latest";
    protected const string PostgresImage = "postgres:13-alpine";
    protected const string NginxImage = "nginx:alpine";

    /// <summary>Label applied to all test-created containers for easy cleanup.</summary>
    protected const string TestLabelKey = "com.fluentdocker.test";
    protected const string TestLabelValue = "integration";

    public async ValueTask InitializeAsync()
    {
      // Ensure Linux daemon mode on Windows
      if (FdOs.IsWindows())
      {
        // Will attempt to switch to Linux mode
      }

      Kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli(DriverId, d => d.AsDefault())
          .BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
      GC.SuppressFinalize(this);

      // Safety-net reap by label (TESTS-2): the base helpers create test-labeled containers,
      // networks, and volumes; a test that fails mid-run before its own finally would otherwise
      // leak them, accumulating across a failing run and eventually exhausting the host/CI. Reap
      // here — while the kernel is still alive — regardless of per-test cleanup, then dispose.
      if (Kernel != null)
      {
        try
        { await ReapTestResourcesAsync().ConfigureAwait(false); }
        catch { /* best-effort: never let teardown reaping fail a run */ }
        Kernel.Dispose();
      }
    }

    private async Task ReapTestResourcesAsync()
    {
      await Utilities.TestContainerUtils
          .RemoveContainersByLabelAsync(Kernel, DriverId, TestLabelKey, TestLabelValue)
          .ConfigureAwait(false);

      var networks = await NetworkDriver.ListAsync(Context,
          new NetworkListFilter { Labels = new Dictionary<string, string> { [TestLabelKey] = TestLabelValue } })
          .ConfigureAwait(false);
      if (networks.Success && networks.Data != null)
        foreach (var network in networks.Data)
          try
          { await NetworkDriver.RemoveAsync(Context, network.Id).ConfigureAwait(false); }
          catch { /* ignore */ }

      var volumes = await VolumeDriver.ListAsync(Context,
          new VolumeListFilter { Labels = new Dictionary<string, string> { [TestLabelKey] = TestLabelValue } })
          .ConfigureAwait(false);
      if (volumes.Success && volumes.Data != null)
        foreach (var volume in volumes.Data)
          try
          { await VolumeDriver.RemoveAsync(Context, volume.Name, force: true).ConfigureAwait(false); }
          catch { /* ignore */ }
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
    protected async Task EnsureImageAsync(string image, bool force = false)
    {
      if (!force)
      {
        var existing = await ImageDriver.ListAsync(Context,
            new ImageListFilter { Reference = image });
        if (existing.Success && existing.Data.Count > 0)
          return;
      }

      var parts = image.Split(':');
      var name = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "latest";

      await ImageDriver.PullAsync(Context, name, tag);
    }

    /// <summary>
    /// Creates and runs a container, returning its ID.
    /// </summary>
    protected async Task<string> RunContainerAsync(string image, ContainerCreateConfig? config = null)
    {
      config ??= new ContainerCreateConfig();
      config.Image = image;
      config.Detach = true;
      config.Labels ??= [];
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
    protected async Task<string> CreateNetworkAsync(string name, NetworkCreateConfig? config = null)
    {
      config ??= new NetworkCreateConfig();
      config.Name = name;
      config.Labels[TestLabelKey] = TestLabelValue;

      var result = await NetworkDriver.CreateAsync(Context, config);
      Assert.True(result.Success, $"Failed to create network: {result.Error}");
      return result.Data.Id;
    }

    /// <summary>
    /// Removes a network.
    /// </summary>
    protected async Task RemoveNetworkAsync(string? networkId)
    {
      if (!string.IsNullOrEmpty(networkId))
      {
        await NetworkDriver.RemoveAsync(Context, networkId);
      }
    }

    /// <summary>
    /// Creates a volume and returns its name.
    /// </summary>
    protected async Task<string> CreateVolumeAsync(string? name = null)
    {
      var config = new VolumeCreateConfig { Name = name ?? $"test-vol-{Guid.NewGuid():N}" };
      config.Labels[TestLabelKey] = TestLabelValue;
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
    protected static string UniqueName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}"[..20];
  }
}

