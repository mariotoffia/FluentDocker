using System;
using System.IO;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public sealed class DockerBinariesResolverProductionReadinessTests
  {
    [Fact]
    public void Constructor_DoesNotRunComposeVersionProbe()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = CreateOutputDirectory();
      var marker = Path.Combine(dir, "compose-probed");
      WriteExecutable(Path.Combine(dir, "docker"), $"""
#!/bin/sh
touch '{marker}'
exit 0
""");

      _ = new DockerBinariesResolver(new BinaryConfiguration { SearchPaths = [dir] });

      Assert.False(File.Exists(marker));
    }

    [Fact]
    public void ResolveFromPaths_IgnoresNonExecutableDockerOnUnix()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("Unix executable bits; not applicable on Windows");

      var first = CreateOutputDirectory();
      var second = CreateOutputDirectory();
      File.WriteAllText(Path.Combine(first, "docker"), "not executable");
      WriteExecutable(Path.Combine(second, "docker"), "#!/bin/sh\nexit 0\n");

      var resolver = new DockerBinariesResolver(new BinaryConfiguration { SearchPaths = [first, second] });

      Assert.Equal(second, resolver.MainDockerClient.Path);
    }

    private static string CreateOutputDirectory()
    {
      var dir = Path.Combine(
          Directory.GetCurrentDirectory(),
          ".out",
          "docker-binaries-resolver-production",
          Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
  }
}
