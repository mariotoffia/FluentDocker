using System;
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
  /// Tests that failing Podman container operations attach an <see cref="ErrorContext"/>
  /// carrying stdout/stderr/exit code (FIX-8) rather than a bare error string. A POSIX shell
  /// stands in for the podman binary so the spawned command fails deterministically; skipped
  /// on Windows.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliErrorContextTests
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
    public async Task FailedStart_PopulatesErrorContext_WithExitAndStderr()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = CreateShellContainerDriver();

      // `sh start <id>` makes sh treat "start" as a script file it cannot open -> non-zero exit
      // with stderr, exercising the failure path with a populated ErrorContext.
      var response = await driver.StartAsync(
          new DriverContext("podman"), "no-such-container", TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.NotNull(response.ErrorContext);
      Assert.Equal("StartContainer", response.ErrorContext.Operation);
      Assert.NotNull(response.ErrorContext.ExitCode);
      Assert.NotEqual(0, response.ErrorContext.ExitCode);
      Assert.False(string.IsNullOrEmpty(response.ErrorContext.StdErr));
    }
  }
}
