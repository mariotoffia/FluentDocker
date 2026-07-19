using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Tests that Docker CLI driver command methods rethrow cancellation
  /// (<see cref="OperationCanceledException"/>) instead of swallowing it into a false
  /// <c>CommandResponse.Fail</c> (FIX-2). Uses a real POSIX shell as a stand-in binary so
  /// the buffered command path actually runs; skipped on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerCliCancellationRethrowTests
  {
    private static DockerCliContainerDriver CreateShellContainerDriver()
    {
      var resolver = new Mock<IBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Returns(new DockerBinary("/bin", "sh", SudoMechanism.None, null!, DockerBinaryType.DockerClient));
      var driver = new DockerCliContainerDriver(resolver.Object);
      driver.Initialize(new DriverContext("docker"));
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
          driver.StartAsync(new DriverContext("docker"), "some-container", cts.Token));
    }
  }
}
