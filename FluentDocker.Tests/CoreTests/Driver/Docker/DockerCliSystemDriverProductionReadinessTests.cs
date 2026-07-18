using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliSystemDriver"/>: per-call context/timeout
  /// overrides, preserved CLI error detail on ping, buffered-timeout and cancellation behaviour of
  /// daemon switching, and malformed-JSON parse-failure messages for info/version.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliSystemDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task PerCallContext_OverridesComponentHost_AndRequestTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      // The fake sleeps far longer than the per-call timeout so the command is always ended by the
      // timeout, never by its own exit. The per-call timeout (8s) must comfortably exceed worst-case
      // process-spawn latency (~3.6s p99 under a saturated parallel run) so the child reliably runs
      // its first line — the `printf` that records the args — before the driver kills it; otherwise
      // args.txt is never written. It still fires well before the 30s sleep and stays below the
      // component timeout, proving the per-call override.
      var docker = CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
sleep 30
echo '{}'
exit 0
""");
      var driver = new DockerCliSystemDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker")
      {
        Host = "tcp://component:2375",
        RequestTimeout = TimeSpan.FromSeconds(60)
      });

      var result = await driver.GetInfoAsync(new DriverContext("docker")
      {
        Host = "tcp://per-call:2375",
        RequestTimeout = TimeSpan.FromSeconds(8)
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
      await WaitForFileAsync(record);
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("tcp://per-call:2375", args);
      Assert.DoesNotContain("tcp://component:2375", args);
    }

    [Fact]
    public async Task PingAsync_PreservesCliErrorDetail()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo 'permission denied while connecting to Docker daemon' 1>&2
exit 42
""");
      var driver = new DockerCliSystemDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PingAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(42, result.ExitCode);
      Assert.Contains("permission denied", result.Error, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("permission denied", result.ErrorContext.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchDaemonAsync_UsesBufferedTimeoutForDefaultToken()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 30
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.SwitchDaemonAsync(
          new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(200) },
          CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.Timeout, result.ErrorCode);
    }

    [Fact]
    public async Task SystemGetInfoAsync_MalformedJsonReturnsParseFailureMessage()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "info" ]; then
  printf '{'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetInfoAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("System info JSON parsing failed", result.Error);
    }

    [Fact]
    public async Task SystemGetVersionAsync_MalformedJsonReturnsParseFailureMessage()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "version" ]; then
  printf '{'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetVersionAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("Docker version JSON parsing failed", result.Error);
    }

    [Fact]
    public async Task SwitchDaemonAsync_CancellationDoesNotWaitForPollingInterval()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 30
exit 0
""")));
      using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50));
      var sw = Stopwatch.StartNew();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SwitchDaemonAsync(new DriverContext("docker"), cts.Token));

      Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), $"Cancellation took {sw.Elapsed}.");
    }
  }
}
