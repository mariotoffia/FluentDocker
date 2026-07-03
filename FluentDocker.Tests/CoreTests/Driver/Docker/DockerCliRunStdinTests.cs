using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public sealed class DockerCliRunStdinTests
  {
    [Fact]
    public async Task RunAsync_ForegroundInteractive_RedirectsAndClosesStdin()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = Path.Combine(Directory.GetCurrentDirectory(), ".out", "docker-run-stdin", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var docker = Path.Combine(dir, "docker");
      File.WriteAllText(docker, """
#!/bin/sh
echo abc123 > "$3"
[ -p /dev/fd/0 ] && printf pipe || printf inherited
exit 0
""");
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new Resolver(docker));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false,
        Interactive = true
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("pipe", result.Data.Output);
    }

    private sealed class Resolver(string path) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(
          Path.GetDirectoryName(path) ?? ".",
          Path.GetFileName(path),
          SudoMechanism.None,
          null!,
          DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCompose => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
