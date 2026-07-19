using System;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliAuthDriver"/>: preserving docker's stderr
  /// and exit code when a large password is piped over stdin, and forcing UTF-8 stdin encoding
  /// regardless of the ambient console encoding.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliAuthDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task LoginAsync_BrokenStdinPipe_PreservesDockerStderrAndExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliAuthDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'registry rejected token' >&2
exit 9
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.LoginAsync(
          new DriverContext("docker"),
          new RegistryLoginConfig
          {
            Username = "user",
            Password = new string('å', 5 * 1024 * 1024),
            PasswordStdin = true
          },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(9, result.ExitCode);
      Assert.Contains("registry rejected token", result.Error);
      Assert.DoesNotContain("Broken pipe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_PasswordStdin_UsesUtf8Encoding()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var original = Console.InputEncoding;
      try
      {
        Console.InputEncoding = Encoding.Latin1;
        var driver = new DockerCliAuthDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
hex=$(od -An -tx1 | tr -d ' \n')
[ "$hex" = "70c3a47373" ] || { echo "$hex" >&2; exit 8; }
exit 0
""")));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.LoginAsync(
            new DriverContext("docker"),
            new RegistryLoginConfig { Username = "user", Password = "päss", PasswordStdin = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
      }
      finally
      {
        Console.InputEncoding = original;
      }
    }
  }
}
