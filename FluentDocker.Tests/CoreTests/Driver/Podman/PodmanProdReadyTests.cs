using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public sealed class PodmanProdReadyTests
  {
    [Fact]
    public async Task StopPodAsync_WhenPodmanSocketIsUnavailable_ReturnsMachineNotRunning()
    {
      RequirePosixShellFixture();

      var context = MachineManagedContext();
      var driver = new PodmanCliPodDriver(new PodmanResolver(CreateFailingPodmanDirectory("pod-stop")));
      driver.Initialize(context);

      var result = await driver.StopPodAsync(
          context,
          "web",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Machine.NotRunning, result.ErrorCode);
    }

    [Fact]
    public async Task PushManifestAsync_WhenPodmanSocketIsUnavailable_ReturnsMachineNotRunning()
    {
      RequirePosixShellFixture();

      var context = MachineManagedContext();
      var driver = new PodmanCliManifestDriver(new PodmanResolver(CreateFailingPodmanDirectory("manifest-push")));
      driver.Initialize(context);

      var result = await driver.PushAsync(
          context,
          new ManifestPushConfig
          {
            ListName = "localhost/list:latest",
            Destination = "docker://registry.example.com/list:latest"
          },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Machine.NotRunning, result.ErrorCode);
    }

    [Fact]
    public void Resolver_WhenConfiguredBinaryIsPodmanRemote_RejectsItAccurately()
    {
      var dir = CreateOutputDirectory("podman-remote");
      WriteExecutable(Path.Combine(dir, OperatingSystem.IsWindows() ? "podman-remote.exe" : "podman-remote"), string.Empty);

      var ex = Assert.Throws<DriverNotAvailableException>(() =>
          new PodmanBinariesResolver(new PodmanBinaryConfiguration
          {
            BinaryName = "podman-remote",
            SearchPaths = [dir]
          }));

      Assert.Equal("podman-remote", ex.DriverId);
      Assert.Contains("remote client", ex.Message);
      Assert.DoesNotContain("Failed to find podman client binary", ex.Message);
    }

    [Fact]
    public async Task CreateContainerAsync_WithHealthCheckNone_EmitsNoHealthcheck()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("healthcheck-none");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.CreateAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            HealthCheck = new HealthCheckConfig
            {
              Test = ["NONE"]
            }
          },
          TestContext.Current.CancellationToken);

      var args = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
      Assert.True(result.Success, result.Error);
      Assert.Contains("--no-healthcheck", args);
      Assert.DoesNotContain("--health-cmd", args);
      Assert.DoesNotContain("NONE", args);
    }

    private static DriverContext MachineManagedContext()
    {
      return new DriverContext("podman")
      {
        AutoStartMachine = new AutoStartMachineConfig()
      };
    }

    private static string CreateFailingPodmanDirectory(string name)
    {
      var dir = CreateOutputDirectory(name);
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          echo 'Cannot connect to Podman socket' >&2
          exit 125
          """);
      return dir;
    }

    private static string CreateOutputDirectory(string name)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RequirePosixShellFixture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");
    }

    private sealed class PodmanResolver(string directory) : IPodmanBinaryResolver
    {
      private readonly PodmanBinary _binary = new(directory, "podman", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient);

      public PodmanBinary[] Binaries => [_binary];

      public PodmanBinary MainPodmanClient => _binary;

      public PodmanBinary PodmanRemote => _binary;

      public PodmanBinary Resolve(string binary) => _binary;

      public string ResolveBinaryPath(string podmanCommand) => _binary.FqPath;
    }
  }
}
