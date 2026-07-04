using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliChunk9ProductionReadinessTests
  {
    [Fact]
    public async Task InfoAsync_ReadsRootlessFromHostSecurity()
    {
      RequirePosixShellFixture();
      var driver = CreateSystemDriver(Return("""
          echo '{"host":{"security":{"rootless":true}}}'
          """));

      var result = await driver.GetInfoAsync(
          new DriverContext("podman"), TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.True(result.Data.Rootless);
    }

    [Fact]
    public async Task RunAsync_NonDetachedMergesStdoutStderrAndReadsCidFile()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-cid-scratch");
      var originalTempPath = DirectoryHelper.GetTempPath;
      DirectoryHelper.GetTempPath = () => scratch;
      try
      {
        var driver = CreateContainerDriver(Return("""
            cid=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--cidfile' ]; then shift; cid="$1"; fi
              shift
            done
            [ -n "$cid" ] && printf '%s' 'ctr-from-cidfile' > "$cid"
            printf '%s' 'stdout-line'
            printf '%s' 'stderr-line' >&2
            """));

        var result = await driver.RunAsync(
            new DriverContext("podman"),
            new ContainerCreateConfig { Image = "alpine", Detach = false, Command = ["true"] },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal("ctr-from-cidfile", result.Data.Id);
        Assert.Equal("stdout-line\nstderr-line", result.Data.Output);
      }
      finally
      {
        DirectoryHelper.GetTempPath = originalTempPath;
      }
    }

    [Fact]
    public async Task CreateAsync_PodmanConnectionFailureUsesTransientErrorCode()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          echo 'Cannot connect to Podman. Please verify your connection to the Linux system.' >&2
          exit 125
          """));

      var result = await driver.CreateAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig { Image = "alpine" },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
      Assert.True(new DriverException(result.Error, result.ErrorCode).IsTransient);
    }

    [Fact]
    public void ExecSuccessfulExitWithPodmanLikeStderr_IsNotInfraFailure()
    {
      Assert.False(PodmanCliContainerDriver.IsExecInfrastructureFailure(
          0, "", "Error: unable to exec but command succeeded"));
    }

    [Fact]
    public async Task GetLogsAsync_MergesStdoutAndStderrWithSeparator()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          printf '%s' 'stdout-line'
          printf '%s' 'stderr-line' >&2
          """));

      var result = await driver.GetLogsAsync(
          new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("stdout-line\nstderr-line", result.Data);
    }

    [Fact]
    public async Task RunAsync_ZeroResourceLimitsDoNotEmitFlags()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-zero-resource-args");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          echo 'ctr'
          """);
      var driver = CreateContainerDriverFromDirectory(dir);

      var result = await driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Detach = true,
            MemoryLimit = 0,
            CpuShares = 0,
            CpuQuota = 0,
            ShmSize = 0
          },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--memory", args);
      Assert.DoesNotContain("--cpu-shares", args);
      Assert.DoesNotContain("--cpu-quota", args);
      Assert.DoesNotContain("--shm-size", args);
    }

    [Fact]
    public async Task LoadAsync_ReturnsParsedLoadedImageNames()
    {
      RequirePosixShellFixture();
      var driver = CreateImageDriver(Return("""
          echo 'Loaded image: docker.io/library/alpine:latest'
          echo 'Loaded image ID: sha256:abc123'
          """));

      var result = await driver.LoadAsync(
          new DriverContext("podman"), "image.tar", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["docker.io/library/alpine:latest", "sha256:abc123"], result.Data);
    }

    [Fact]
    public async Task InspectAsync_NoSuchContainerMapsToNotFound()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          echo 'Error: no such object: "missing"' >&2
          exit 125
          """));

      var result = await driver.InspectAsync(
          new DriverContext("podman"), "missing", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task InspectAsync_GenuineFailureStaysInspectFailed()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          echo 'Error: inspect failed for another reason' >&2
          exit 125
          """));

      var result = await driver.InspectAsync(
          new DriverContext("podman"), "broken", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.InspectFailed, result.ErrorCode);
    }

    [Theory]
    [InlineData(false, false, "y")]
    [InlineData(true, true, "")]
    public async Task RemoveMachineAsync_UsesForceOnlyWhenRequested(bool force, bool expectForce, string expectedStdin)
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-machine-rm-force");
      var record = Path.Combine(dir, "args.txt");
      var stdinRecord = Path.Combine(dir, "stdin.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          has_force=0
          for arg in "$@"; do
            [ "$arg" = "-f" ] && has_force=1
          done
          if [ "$has_force" = "1" ]; then
            : > '{stdinRecord}'
          else
            cat > '{stdinRecord}'
          fi
          exit 0
          """);
      var driver = CreateMachineDriverFromDirectory(dir);

      var result = await driver.RemoveAsync(
          new DriverContext("podman"), "vm", force, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
      if (expectForce)
        Assert.Contains("-f", args);
      else
        Assert.DoesNotContain("-f", args);
      Assert.Equal(expectedStdin, (await File.ReadAllTextAsync(stdinRecord, TestContext.Current.CancellationToken)).Trim());
    }

    [Fact]
    public async Task MachineList_PreservesAlreadyByteSizedMemoryAndDisk()
    {
      RequirePosixShellFixture();
      var driver = CreateMachineDriver(Return("""
          echo '[{"Name":"podman-machine-default","Memory":"2147483648","DiskSize":"107374182400"}]'
          """));

      var result = await driver.ListAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var machine = Assert.Single(result.Data);
      Assert.Equal(2147483648L, machine.Memory);
      Assert.Equal(107374182400L, machine.DiskSize);
    }

    private static string Return(string body) => $"""
        #!/bin/sh
        {body}
        """;

    private static PodmanCliContainerDriver CreateContainerDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-chunk9-container", script);
      return CreateContainerDriverFromDirectory(dir);
    }

    private static PodmanCliContainerDriver CreateContainerDriverFromDirectory(string dir)
    {
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-chunk9-image", script);
      var driver = new PodmanCliImageDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliSystemDriver CreateSystemDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-chunk9-system", script);
      var driver = new PodmanCliSystemDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliMachineDriver CreateMachineDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-chunk9-machine", script);
      return CreateMachineDriverFromDirectory(dir);
    }

    private static PodmanCliMachineDriver CreateMachineDriverFromDirectory(string dir)
    {
      var driver = new PodmanCliMachineDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static string CreatePodmanDirectory(string name, string script)
    {
      var dir = CreateOutputDirectory(name);
      WriteExecutable(Path.Combine(dir, "podman"), script);
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
