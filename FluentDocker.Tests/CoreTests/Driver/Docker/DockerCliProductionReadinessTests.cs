using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task ImagePull_StreamsProgress_AndAllowsLargeOutput()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "pull" ]; then
  i=0
  while [ $i -lt 3000 ]; do
    printf 'layer%04d Downloading ' "$i"
    printf '%01990d\n' 0
    i=$((i+1))
  done
  exit 0
fi
exit 2
""");
      var driver = new DockerCliImageDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));
      var progress = new ListProgress<ImagePullProgress>();

      var result = await driver.PullAsync(
          new DriverContext("docker"),
          "alpine",
          "latest",
          progress,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotEmpty(progress.Items);
      Assert.Contains(progress.Items, p => p.Status.Contains("Downloading", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PerCallContext_OverridesComponentHost_AndRequestTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var docker = CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
sleep 2
echo '{}'
exit 0
""");
      var driver = new DockerCliSystemDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker")
      {
        Host = "tcp://component:2375",
        RequestTimeout = TimeSpan.FromSeconds(10)
      });

      var result = await driver.GetInfoAsync(new DriverContext("docker")
      {
        Host = "tcp://per-call:2375",
        RequestTimeout = TimeSpan.FromMilliseconds(1500)
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("tcp://per-call:2375", args);
      Assert.DoesNotContain("tcp://component:2375", args);
    }

    [Fact]
    public async Task ImageProgressFailures_PropagateRealExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
case "$1" in
  pull) echo 'pull failed' 1>&2; exit 17 ;;
  push) echo 'push failed' 1>&2; exit 18 ;;
  build) echo 'build failed' 1>&2; exit 19 ;;
esac
exit 2
""");
      var driver = new DockerCliImageDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));

      var pull = await driver.PullAsync(new DriverContext("docker"), "alpine", "latest", cancellationToken: TestContext.Current.CancellationToken);
      var push = await driver.PushAsync(new DriverContext("docker"), "alpine:latest", cancellationToken: TestContext.Current.CancellationToken);
      var build = await driver.BuildAsync(new DriverContext("docker"), new ImageBuildConfig { BuildContext = "." }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(17, pull.ExitCode);
      Assert.Equal(17, pull.ErrorContext.ExitCode);
      Assert.Equal(18, push.ExitCode);
      Assert.Equal(18, push.ErrorContext.ExitCode);
      Assert.Equal(19, build.ExitCode);
      Assert.Equal(19, build.ErrorContext.ExitCode);
    }

    [Fact]
    public async Task PingAsync_PreservesCliErrorDetail()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo 'permission denied while connecting to Docker daemon' 1>&2
exit 42
""");
      var driver = new DockerCliSystemDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PingAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(42, result.ExitCode);
      Assert.Contains("permission denied", result.Error, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("permission denied", result.ErrorContext.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContainerList_PreservesCreatedAtOffsets()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "ps" ]; then
  printf '%s\n' '{"Command":"sleep","CreatedAt":"2024-01-02T03:04:05+02:00","ID":"offset","Image":"alpine","Names":"offset","State":"running","Status":"Up"}'
  printf '%s\n' '{"Command":"sleep","CreatedAt":"2024-01-02T01:04:05Z","ID":"zulu","Image":"alpine","Names":"zulu","State":"running","Status":"Up"}'
  exit 0
fi
exit 2
""")));
      var context = new DriverContext("docker");
      driver.Initialize(context);

      var result = await driver.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(TimeSpan.FromHours(2), result.Data.Single(c => c.Id == "offset").Created.Offset);
      Assert.Equal(TimeSpan.Zero, result.Data.Single(c => c.Id == "zulu").Created.Offset);
    }

    private static string CreateFakeDocker(string script)
    {
      var directory = TestOutputDirectory();
      var path = Path.Combine(directory, $"fake-docker-{Guid.NewGuid():N}");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      using var chmod = Process.Start(new ProcessStartInfo
      {
        FileName = "chmod",
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "+x", path }
      });
      chmod!.WaitForExit();
      return path;
    }

    private static string CreateRecordingDocker(string record, string output)
    {
      return CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
echo "{{output}}"
exit 0
""");
    }

    private static async Task<string[]> ReadArgsAsync(string record)
    {
      var text = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      return text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string TestOutputDirectory()
    {
      var directory = Path.Combine(Directory.GetCurrentDirectory(), ".out", "docker-cli-production-tests");
      Directory.CreateDirectory(directory);
      return directory;
    }

    private sealed class ListProgress<T> : IProgress<T>
    {
      public List<T> Items { get; } = [];

      public void Report(T value) => Items.Add(value);
    }

    private sealed class ShellDriver : DockerCliDriverBase
    {
      public ShellDriver() : base(new FakeResolver("/bin/sh"))
      {
      }

      public Task<SimpleCommandResult> RunUnbounded(string args, CancellationToken cancellationToken) =>
          ExecuteUnboundedCommandAsync(args, cancellationToken);
    }

    private sealed class FakeResolver : IBinaryResolver
    {
      private readonly DockerBinary _binary;

      public FakeResolver(string path) =>
        _binary = new DockerBinary(
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
