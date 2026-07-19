using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliDriverPack"/>: the supported-interface
  /// collection is materialised once during initialize and returned as a cached instance.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliDriverPackProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task DriverPackGetSupportedInterfaces_ReturnsCachedCollectionAfterInitialize()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = CreateNamedFakeDockerDirectory("""
#!/bin/sh
exit 0
""");
      var pack = new DockerCliDriverPack();

      await pack.InitializeAsync(
          new DriverContext("docker") { SearchPaths = [dir] },
          TestContext.Current.CancellationToken);

      Assert.Same(pack.GetSupportedInterfaces(), pack.GetSupportedInterfaces());
    }

    private static string CreateNamedFakeDockerDirectory(string script)
    {
      var directory = Path.Combine(TestOutputDirectory(), $"named-docker-{Guid.NewGuid():N}");
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, "docker");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      using var chmod = Process.Start(new ProcessStartInfo
      {
        FileName = "chmod",
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "+x", path }
      });
      chmod!.WaitForExit();
      return directory;
    }
  }
}
