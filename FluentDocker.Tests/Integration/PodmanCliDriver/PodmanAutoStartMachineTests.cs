using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Kernel;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration tests for WithAutoStartMachine feature.
  /// Requires Podman to be installed with a running machine.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "LongRunning")]
  public class PodmanAutoStartMachineTests
  {
    [Fact]
    public async Task WithAutoStartMachine_WhenMachineRunning_Succeeds()
    {
      SkipIfPodmanNotAvailable();

      FluentDockerKernel kernel = null;
      try
      {
        kernel = await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d
                .WithAutoStartMachine()
                .AsDefault())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(kernel);

        // Verify the kernel is functional by checking health
        var pack = kernel.GetDriverPack("podman");
        Assert.NotNull(pack);
        var healthy = await pack.IsHealthyAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(healthy, "Podman driver should be healthy after auto-start");
      }
      finally
      {
        kernel?.Dispose();
      }
    }

    [Fact]
    public async Task WithAutoStartMachine_WithConfig_Succeeds()
    {
      SkipIfPodmanNotAvailable();

      FluentDockerKernel kernel = null;
      try
      {
        kernel = await FluentDockerKernel.Create()
            .WithPodmanCli("podman", d => d
                .WithAutoStartMachine(c =>
                {
                  // Don't set MachineName — use default
                  // Don't set CreateIfNotExists — just start existing
                })
                .AsDefault())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(kernel);
      }
      finally
      {
        kernel?.Dispose();
      }
    }

    [Fact]
    public async Task WithAutoStartMachine_NonExistentMachine_ThrowsWhenNoCreate()
    {
      SkipIfPodmanNotAvailable();

      var ex = await Assert.ThrowsAsync<PodmanMachineNotRunningException>(async () =>
      {
        await FluentDockerKernel.Create()
                  .WithPodmanCli("podman", d => d
                      .WithAutoStartMachine(c =>
                      {
                        c.MachineName = "nonexistent-fd-test-" +
                            Guid.NewGuid().ToString("N")[..8];
                        c.CreateIfNotExists = false;
                      })
                      .AsDefault())
                  .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      });

      Assert.Contains("No Podman machine found", ex.Message);
    }

    private static void SkipIfPodmanNotAvailable()
    {
      if (!IsPodmanInstalled())
        throw new SkipException("Podman is not installed");

      if (!IsPodmanMachineRunning())
        throw new PodmanMachineNotRunningException(
            "Podman machine is not running. Start it with: podman machine start");
    }

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

    private static bool IsPodmanMachineRunning()
    {
      try
      {
        var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = "podman",
            Arguments = "info",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();
        process.WaitForExit(10000);
        return process.ExitCode == 0;
      }
      catch
      {
        return false;
      }
    }
  }
}
