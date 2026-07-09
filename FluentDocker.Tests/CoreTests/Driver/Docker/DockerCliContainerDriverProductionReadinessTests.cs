using System;
using System.Collections.Generic;
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
  [Trait("Requires", "PosixShell")]
  public class DockerCliContainerDriverProductionReadinessTests
  {
    [Fact]
    public async Task CreateAsync_RendersAllBuilderContainerOptions()
    {
      if (OperatingSystem.IsWindows())
        return;

      var directory = Path.Combine(".out", "docker-create-args", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf 'container-123\\n'\n",
          TestContext.Current.CancellationToken);
      File.SetUnixFileMode(dockerPath,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.CreateAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Name = "web",
            ExtraHosts = new Dictionary<string, string> { { "db", "10.0.0.2" } },
            NetworkAliases = new Dictionary<string, List<string>> { { "net", ["api"] } },
            CapAdd = ["NET_ADMIN"],
            CapDrop = ["MKNOD"],
            SecurityOpt = ["seccomp=unconfined"],
            ShmSize = 67108864,
            Tmpfs = new Dictionary<string, string> { { "/run", "rw,noexec" } },
            Devices = new Dictionary<string, string> { { "/dev/fuse", "/dev/fuse" } },
            ReadonlyRootfs = true,
            Platform = "linux/arm64",
            Runtime = "runc",
            Interactive = true,
            Tty = true,
            Entrypoint = ["/bin/sh", "-c"],
            Command = ["echo", "hello world"]
          },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.Contains("--add-host", args);
      Assert.Contains("db:10.0.0.2", args);
      Assert.Contains("--network-alias", args);
      Assert.Contains("api", args);
      Assert.Contains("--cap-add", args);
      Assert.Contains("NET_ADMIN", args);
      Assert.Contains("--cap-drop", args);
      Assert.Contains("MKNOD", args);
      Assert.Contains("--security-opt", args);
      Assert.Contains("seccomp=unconfined", args);
      Assert.Contains("--shm-size", args);
      Assert.Contains("67108864", args);
      Assert.Contains("--tmpfs", args);
      Assert.Contains("/run:rw,noexec", args);
      Assert.Contains("--device", args);
      Assert.Contains("/dev/fuse", args);
      Assert.Contains("--read-only", args);
      Assert.Contains("--platform", args);
      Assert.Contains("linux/arm64", args);
      Assert.Contains("--runtime", args);
      Assert.Contains("runc", args);
      Assert.Contains("-i", args);
      Assert.Contains("-t", args);
      Assert.Contains("--entrypoint", args);
      Assert.Contains("/bin/sh", args);
      Assert.Contains("-c", args);
      Assert.Contains("hello world", args);
    }

    [Fact]
    public async Task RunAsync_DoesNotRenderZeroResourceLimits()
    {
      if (OperatingSystem.IsWindows())
        return;

      var directory = Path.Combine(".out", "docker-run-args", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf 'container-123\\n'\n",
          TestContext.Current.CancellationToken);
      File.SetUnixFileMode(dockerPath,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.RunAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Detach = true,
            MemoryLimit = 0,
            CpuShares = 0,
            ShmSize = 0
          },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--memory", args);
      Assert.DoesNotContain("--cpu-shares", args);
      Assert.DoesNotContain("--shm-size", args);
    }

    [Fact]
    public async Task InspectAsync_DockerNameWithLeadingSlash_TrimsToBareName()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-inspect-name", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          "printf '%s\\n' '[{\"Id\":\"abc123\",\"Name\":\"/foo\"}]'\n",
          TestContext.Current.CancellationToken);
      File.SetUnixFileMode(dockerPath,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.InspectAsync(
          new DriverContext("docker"),
          "foo",
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      Assert.Equal("foo", response.Data.Name);
    }

    [Fact]
    public async Task ListAsync_WithLimit_EmitsLastFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-list-limit", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf '%s\\n' '{\"ID\":\"abc123\",\"Image\":\"alpine\",\"Names\":\"foo\",\"State\":\"running\",\"Status\":\"Up\"}'\n",
          TestContext.Current.CancellationToken);
      File.SetUnixFileMode(dockerPath,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.ListAsync(
          new DriverContext("docker"),
          new ContainerListFilter { Limit = 3 },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.Equal(["ps", "--format", "{{json .}}", "--last", "3"], args);
    }

    [Fact]
    public async Task ListAsync_WithNonPositiveLimit_OmitsLastFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-list-limit-zero", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf '%s\\n' '{\"ID\":\"abc123\",\"Image\":\"alpine\",\"Names\":\"foo\",\"State\":\"running\",\"Status\":\"Up\"}'\n",
          TestContext.Current.CancellationToken);
      File.SetUnixFileMode(dockerPath,
          UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.ListAsync(
          new DriverContext("docker"),
          new ContainerListFilter { Limit = 0 },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--last", args);
    }

    private sealed class TestBinaryResolver(string directory) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(directory, "docker", SudoMechanism.None, null!);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
