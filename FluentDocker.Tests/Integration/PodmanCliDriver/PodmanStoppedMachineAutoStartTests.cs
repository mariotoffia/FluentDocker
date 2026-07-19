using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration.PodmanCliDriver
{
  /// <summary>
  /// Integration test for production-grade machine auto-start (P3/P4). When the configured (default)
  /// machine EXISTS but is STOPPED, <c>WithAutoStartMachine</c> must start it AND poll readiness so
  /// the pack is actually healthy on return — not merely "start issued". Tagged PodmanIntegration so
  /// it is excluded from the unit filter; it stops/starts the host machine, so it needs a live
  /// Podman machine to run and is skipped otherwise.
  /// </summary>
  [Collection("PodmanDriver")]
  [Trait("Category", "PodmanIntegration")]
  public class PodmanStoppedMachineAutoStartTests
  {
    [Fact]
    public async Task WithAutoStartMachine_WhenDefaultMachineStopped_StartsAndBecomesHealthy()
    {
      if (!IsPodmanInstalled())
        throw new SkipException("Podman is not installed");
      if (!TryGetFirstMachine(out var machine))
        throw new SkipException("No Podman machine exists to stop then auto-start");

      // Put the machine into the STOPPED state that auto-start must recover from (ignore the exit
      // code — it may already be stopped).
      RunPodman($"machine stop {machine}", 120_000);

      FluentDockerKernel? kernel = null;
      try
      {
        kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
            .WithPodmanCli("podman", d => d
                .WithAutoStartMachine() // null MachineName => podman's real default machine (P3)
                .AsDefault())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        var pack = kernel.GetDriverPack("podman");
        Assert.NotNull(pack);

        // The P3 readiness poll must guarantee the machine actually answers `info` before returning.
        var healthy = await pack.IsHealthyAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(healthy, "Auto-start must start the stopped machine and wait until it is ready");
      }
      finally
      {
        kernel?.Dispose();
      }
    }

    private static bool TryGetFirstMachine(out string name)
    {
      name = null!;
      var (exit, stdout) = RunPodman("machine list --format {{.Name}}", 15_000);
      if (exit != 0)
        return false;

      foreach (var line in stdout.Split('\n'))
      {
        var trimmed = line.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
          name = trimmed;
          return true;
        }
      }

      return false;
    }

    private static (int ExitCode, string StdOut) RunPodman(string arguments, int timeoutMs)
    {
      try
      {
        var process = new Process
        {
          StartInfo = new ProcessStartInfo
          {
            FileName = "podman",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
          }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, stdout);
      }
      catch
      {
        return (-1, string.Empty);
      }
    }

    private static bool IsPodmanInstalled()
    {
      var (exit, _) = RunPodman("--version", 5_000);
      return exit == 0;
    }
  }
}
