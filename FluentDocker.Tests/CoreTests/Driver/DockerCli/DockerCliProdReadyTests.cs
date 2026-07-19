using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerCli
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class DockerCliProdReadyTests
  {
    [Fact]
    public async Task ImagesAsync_MalformedArrayJson_ReturnsFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker(
          "compose-images-malformed-array",
          """
          #!/bin/sh
          printf '[{"Container":"web"}\n'
          exit 0
          """)));
      driver.Initialize(Context());

      var response = await driver.ImagesAsync(
          Context(),
          new ComposeFileConfig(),
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Compose.ImagesFailed, response.ErrorCode);
    }

    [Fact]
    public async Task NetworkInspectAsync_EnableIPv6_PopulatesIPv6()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliNetworkDriver(new FakeResolver(CreateFakeDocker(
          "network-inspect-ipv6",
          """
          #!/bin/sh
          printf '%s\n' '[{"Id":"net123","Name":"v6net","Driver":"bridge","EnableIPv6":true}]'
          exit 0
          """)));
      driver.Initialize(Context());

      var response = await driver.InspectAsync(
          Context(),
          "v6net",
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      Assert.True(response.Data!.IPv6);
    }

    [Fact]
    public async Task CreateAsync_HealthCheckNone_UsesNoHealthcheck()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(OutputDirectory("healthcheck-none"), "args.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record)));
      driver.Initialize(Context());

      var response = await driver.CreateAsync(
          Context(),
          new ContainerCreateConfig
          {
            Image = "alpine",
            HealthCheck = new HealthCheckConfig { Test = ["NONE"] }
          },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("--no-healthcheck", args);
      Assert.DoesNotContain("--health-cmd", args);
    }

    private static DriverContext Context() => new("docker");

    private static string CreateRecordingDocker(string record)
    {
      return CreateFakeDocker(
          "recording",
          $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          printf 'container-123\n'
          """);
    }

    private static string CreateFakeDocker(string name, string script)
    {
      var path = Path.Combine(OutputDirectory(name), "docker");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      // Unix-only: these tests are skipped on Windows (see each [Fact]); the guard also
      // satisfies the CA1416 platform analyzer, which cannot see the cross-method Assert.Skip.
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      return path;
    }

    private static string OutputDirectory(string name)
    {
      var directory = Path.Combine(
          Directory.GetCurrentDirectory(),
          ".out",
          "docker-cli-prod-ready",
          name,
          Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      return directory;
    }

    private sealed class FakeResolver(string binaryPath) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(
          Path.GetDirectoryName(binaryPath) ?? ".",
          Path.GetFileName(binaryPath),
          SudoMechanism.None,
          null!,
          DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
