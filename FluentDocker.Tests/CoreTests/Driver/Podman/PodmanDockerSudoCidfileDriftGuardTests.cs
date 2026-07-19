using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
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
  public class PodmanDockerSudoCidfileDriftGuardTests
  {
    [Fact]
    public async Task DriftGuard_SudoCommandAndRunCidfile_StayAlignedAcrossDockerAndPodman()
    {
      RequirePosixShellFixture();
      foreach (var sudo in Enum.GetValues<SudoMechanism>())
        Assert.Equal(
            InvokeSudoCommand(typeof(DockerCliDriverBase), sudo),
            InvokeSudoCommand(typeof(PodmanCliDriverBase), sudo));

      var oldTempPath = DirectoryHelper.GetTempPath;
      var tempRoot = CreateOutputDirectory("drift-cidfile-temp");
      DirectoryHelper.GetTempPath = () => tempRoot;
      try
      {
        foreach (var detach in new[] { false, true })
        {
          var dockerArgs = await RunDockerAsync(detach);
          var podmanArgs = await RunPodmanAsync(detach);

          Assert.Contains("--cidfile", dockerArgs);
          Assert.Contains("--cidfile", podmanArgs);
        }
      }
      finally
      {
        DirectoryHelper.GetTempPath = oldTempPath;
      }
    }

    private static (string FileName, string Arguments, string PasswordForStdin) InvokeSudoCommand(
        Type driverBaseType, SudoMechanism sudo)
    {
      // Bind the 4-arg overload explicitly: the Docker side also has a 5-arg overload that
      // forwards caller environment names through sudo (--preserve-env, DC-2), which a
      // name-only lookup would make ambiguous.
      var method = driverBaseType.GetMethod(
          "BuildSudoCommand",
          BindingFlags.NonPublic | BindingFlags.Static,
          [typeof(string), typeof(string), typeof(SudoMechanism), typeof(string)]);
      Assert.NotNull(method);
      return ((string FileName, string Arguments, string PasswordForStdin))method.Invoke(
          null,
          ["/opt/cli bin", "info --format json", sudo, "secret"])!;
    }

    private static async Task<string> RunDockerAsync(bool detach)
    {
      var dir = CreateOutputDirectory($"drift-docker-{detach}");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "docker"), RecordingRunScript(record));
      var driver = new DockerCliContainerDriver(new DockerResolver(dir));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig { Image = "alpine", Detach = detach },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      return await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
    }

    private static async Task<string> RunPodmanAsync(bool detach)
    {
      var dir = CreateOutputDirectory($"drift-podman-{detach}");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), RecordingRunScript(record));
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig { Image = "alpine", Detach = detach },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      return await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
    }

    private static string RecordingRunScript(string record) => $"""
        #!/bin/sh
        printf '%s\n' "$@" > '{record}'
        cid=''
        while [ "$#" -gt 0 ]; do
          if [ "$1" = '--cidfile' ]; then shift; cid="$1"; fi
          shift
        done
        [ -n "$cid" ] && printf '%s' 'ctr-from-cidfile' > "$cid"
        echo 'ctr-from-output'
        exit 0
        """;

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

    private sealed class DockerResolver(string directory) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(directory, "docker", SudoMechanism.None, null!);
      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
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
