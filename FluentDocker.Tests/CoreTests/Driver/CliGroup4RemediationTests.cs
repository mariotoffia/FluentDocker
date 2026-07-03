using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public class CliGroup4RemediationTests
  {
    [Fact]
    public async Task DockerComposeExec_NonZeroInContainerExit_ReturnsFailureWithExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateDockerComposeDriver("""
          #!/bin/sh
          echo "exec stderr" >&2
          exit 5
          """);

      var response = await driver.ExecuteAsync(
          new DriverContext("docker"),
          new ComposeExecConfig { Service = "web", Tty = false, Command = ["sh", "-c", "exit 5"] },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(5, response.ExitCode);
      Assert.Contains("exec stderr", response.Error);
    }

    [Fact]
    public async Task DockerComposeBuildAndPull_IgnoreBufferedRequestTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreateDockerComposeDriver("""
          #!/bin/sh
          sleep 0.3
          exit 0
          """);
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(50) };

      var build = await driver.BuildAsync(
          context, new ComposeBuildConfig(), TestContext.Current.CancellationToken);
      var pull = await driver.PullAsync(
          context, new ComposePullConfig(), TestContext.Current.CancellationToken);

      Assert.True(build.Success, build.Error);
      Assert.True(pull.Success, pull.Error);
    }

    [Fact]
    public async Task DockerCopy_HostPathStartingWithDash_FailsBeforeProcessStarts()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var dir = CreateOutputDirectory("docker-copy-dash");
      var marker = Path.Combine(dir, "invoked");
      WriteExecutable(Path.Combine(dir, "docker"), $"""
          #!/bin/sh
          touch '{marker}'
          exit 0
          """);
      var driver = new DockerCliContainerDriver(new DockerResolver(dir));
      driver.Initialize(new DriverContext("docker"));

      var to = await driver.CopyToAsync(
          new DriverContext("docker"), "container", "-L", "/data", TestContext.Current.CancellationToken);
      var from = await driver.CopyFromAsync(
          new DriverContext("docker"), "container", "/data", "-L", TestContext.Current.CancellationToken);

      Assert.False(to.Success);
      Assert.False(from.Success);
      Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task PodmanWait_UnparseableOutput_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreatePodmanContainerDriver("""
          #!/bin/sh
          echo garbage
          exit 0
          """);

      var response = await driver.WaitAsync(
          new DriverContext("podman"), "container", TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Container.WaitFailed, response.ErrorCode);
    }

    [Fact]
    public async Task PodmanContainer_PerCallContextHost_ReachesProcessArgs()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var dir = CreateOutputDirectory("podman-context");
      var argsPath = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{argsPath}'
          exit 0
          """);
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman") { Host = "tcp://component:1234" });

      var response = await driver.StartAsync(
          new DriverContext("podman") { Host = "tcp://per-call:5678" },
          "container",
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.Contains("--url", args);
      Assert.Contains("tcp://per-call:5678", args);
      Assert.DoesNotContain("tcp://component:1234", args);
    }

    [Fact]
    public async Task PodmanStreamStats_BufferOverflow_ResetsAndYieldsNextArray()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var driver = CreatePodmanStreamDriver("""
          #!/bin/sh
          python3 - <<'PY'
          print('[' + 'a' * (4 * 1024 * 1024 + 8))
          print('[{"id":"rec","name":"ok","cpu_percent":"1.00%","mem_usage":"1MiB / 1GiB","mem_percent":"0.10%","net_io":"0B / 0B","block_io":"0B / 0B","pids":"1"}]')
          PY
          """);
      var stats = new List<ContainerStats>();

      await foreach (var item in driver.StreamStatsAsync(
          new DriverContext("podman"),
          config: new StreamStatsConfig { Stream = false },
          cancellationToken: TestContext.Current.CancellationToken))
        stats.Add(item);

      Assert.Single(stats);
      Assert.Equal("rec", stats[0].ContainerId);
    }

    [Fact]
    public async Task DockerImageRemove_NoSuchImage_MapsNotFound()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var dir = CreateOutputDirectory("docker-image-rm");
      WriteExecutable(Path.Combine(dir, "docker"), """
          #!/bin/sh
          echo "Error: No such image: missing:latest" >&2
          exit 1
          """);
      var driver = new DockerCliImageDriver(new DockerResolver(dir));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.RemoveAsync(
          new DriverContext("docker"), "missing:latest", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Image.NotFound, response.ErrorCode);
    }

    private static DockerCliComposeDriver CreateDockerComposeDriver(string script)
    {
      var dir = CreateOutputDirectory("docker-compose");
      WriteExecutable(Path.Combine(dir, "docker"), script);
      var driver = new DockerCliComposeDriver(new DockerResolver(dir));
      driver.Initialize(new DriverContext("docker"));
      return driver;
    }

    private static PodmanCliContainerDriver CreatePodmanContainerDriver(string script)
    {
      var dir = CreateOutputDirectory("podman-container");
      WriteExecutable(Path.Combine(dir, "podman"), script);
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliStreamDriver CreatePodmanStreamDriver(string script)
    {
      var dir = CreateOutputDirectory("podman-stream-recovery");
      WriteExecutable(Path.Combine(dir, "podman"), script);
      var driver = new PodmanCliStreamDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
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

    private sealed class DockerResolver(string directory) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(directory, "docker", SudoMechanism.None, null!);
      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCompose => _binary;
      public DockerBinary MainDockerCli => _binary;
      public bool IsDockerComposeAvailable => true;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
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
