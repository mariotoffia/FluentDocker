using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Base class for PodmanCliDriver integration tests.
  /// Skips tests gracefully if Podman is not installed.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public abstract class PodmanDriverTestBase : IAsyncLifetime
  {
    protected FluentDockerKernel Kernel { get; private set; }
    protected string DriverId => "podman";
    protected DriverContext Context => new DriverContext(DriverId);

    protected const string TestImage = "alpine:latest";
    protected const string NginxImage = "nginx:alpine";

    /// <summary>Label applied to all test-created containers for easy cleanup.</summary>
    protected const string TestLabelKey = "com.fluentdocker.test";
    protected const string TestLabelValue = "integration";

    public async Task InitializeAsync()
    {
      if (!IsPodmanInstalled())
      {
        throw new SkipException("Podman is not installed or not in PATH");
      }

      Kernel = await FluentDockerKernel.Create()
          .WithPodmanCli(DriverId, d => d
              .WithAutoStartMachine(c =>
              {
                c.CreateIfNotExists = true;
                c.InitCpus = 2;
                c.InitMemoryMiB = 2048;
              })
              .AsDefault())
          .BuildAsync();
    }

    public Task DisposeAsync()
    {
      Kernel?.Dispose();
      return Task.CompletedTask;
    }

    protected IContainerDriver ContainerDriver => Kernel.SysCtl<IContainerDriver>(DriverId);
    protected INetworkDriver NetworkDriver => Kernel.SysCtl<INetworkDriver>(DriverId);
    protected IVolumeDriver VolumeDriver => Kernel.SysCtl<IVolumeDriver>(DriverId);
    protected IImageDriver ImageDriver => Kernel.SysCtl<IImageDriver>(DriverId);
    protected ISystemDriver SystemDriver => Kernel.SysCtl<ISystemDriver>(DriverId);

    protected IPodmanKubernetesDriver KubernetesDriver
    {
      get
      {
        Kernel.TrySysCtl<IPodmanKubernetesDriver>(DriverId, out var driver);
        return driver;
      }
    }

    protected IPodmanPodDriver PodDriver
    {
      get
      {
        Kernel.TrySysCtl<IPodmanPodDriver>(DriverId, out var driver);
        return driver;
      }
    }

    protected IPodmanMachineDriver MachineDriver
    {
      get
      {
        Kernel.TrySysCtl<IPodmanMachineDriver>(DriverId, out var driver);
        return driver;
      }
    }

    protected IPodmanManifestDriver ManifestDriver
    {
      get
      {
        Kernel.TrySysCtl<IPodmanManifestDriver>(DriverId, out var driver);
        return driver;
      }
    }

    protected async Task RemovePodAsync(string podName)
    {
      if (!string.IsNullOrEmpty(podName))
        await PodDriver.RemovePodAsync(Context, podName, force: true);
    }

    protected async Task EnsureImageAsync(string image)
    {
      var parts = image.Split(':');
      var name = parts[0];
      var tag = parts.Length > 1 ? parts[1] : "latest";
      await ImageDriver.PullAsync(Context, name, tag);
    }

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

    protected async Task RemoveContainerAsync(string containerId)
    {
      if (!string.IsNullOrEmpty(containerId))
        await ContainerDriver.RemoveAsync(Context, containerId, force: true, removeVolumes: true);
    }

    protected string UniqueName(string prefix = "test") =>
        $"{prefix}-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Checks if the podman binary is installed (local-only, no server contact).
    /// </summary>
    private static bool IsPodmanInstalled()
    {
      try
      {
        var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = "podman",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();
        process.WaitForExit(5000);
        return process.ExitCode == 0;
      }
      catch
      {
        return false;
      }
    }

  }

  /// <summary>
  /// Exception used to skip tests when Podman is not available.
  /// Uses xUnit dynamic skip convention ($XunitDynamicSkip$) so
  /// tests are reported as skipped rather than failed.
  /// </summary>
  public class SkipException : Exception
  {
    public SkipException(string message) : base("$XunitDynamicSkip$" + message) { }
  }
}
