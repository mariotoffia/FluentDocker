using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests that Podman CLI driver command methods rethrow cancellation
  /// (<see cref="OperationCanceledException"/>) instead of swallowing it into a false
  /// <c>CommandResponse.Fail</c> (FIX-2). Uses a real POSIX shell as a stand-in podman binary
  /// so the buffered command path actually runs; skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliCancellationRethrowTests
  {
    private static PodmanCliContainerDriver CreateShellContainerDriver()
    {
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new PodmanBinary("/bin", "sh", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient));
      var driver = new PodmanCliContainerDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    [Fact]
    public async Task StartAsync_CallerAlreadyCancelled_RethrowsInsteadOfFailResponse()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellContainerDriver();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      // The generic catch in StartAsync would otherwise convert the OperationCanceledException
      // into CommandResponse<Unit>.Fail; FIX-2's rethrow guard must surface cancellation.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.StartAsync(new DriverContext("podman"), "some-container", cts.Token));
    }

    [Fact]
    public async Task StopAsync_CallerAlreadyCancelled_RethrowsInsteadOfFailResponse()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellContainerDriver();
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      // StopAsync routes through the unbounded path; cancellation must still propagate.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.StopAsync(new DriverContext("podman"), "some-container", 10, cts.Token));
    }
  }
}
