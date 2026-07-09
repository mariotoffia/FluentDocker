using System;
using System.Collections.Generic;
using System.Globalization;
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
  public class ProdReadyDockerCliFixesTests
  {
    [Fact]
    public void BuildStreamLogsArgs_RendersNumericTailWithInvariantCulture()
    {
      var previousCulture = CultureInfo.CurrentCulture;
      var previousUiCulture = CultureInfo.CurrentUICulture;
      try
      {
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        CultureInfo.CurrentUICulture = new CultureInfo("sv-SE");

        var args = DockerCliStreamDriver.BuildStreamLogsArgs(
            "container",
            new StreamLogsConfig { Tail = -1 });

        Assert.Contains("--tail -1", args, StringComparison.Ordinal);
        Assert.DoesNotContain("−1", args, StringComparison.Ordinal);
      }
      finally
      {
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
      }
    }

    [Fact]
    public async Task InspectAsync_NoSuchContainerStderr_ReturnsContainerNotFound()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("inspect-no-such-container", """
          #!/bin/sh
          printf 'Error: No such container: missing\n' >&2
          exit 1
          """);
      var driver = new DockerCliContainerDriver(fixture.Resolver);
      driver.Initialize(Context());

      var response = await driver.InspectAsync(
          Context(),
          "missing",
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, response.ErrorCode);
    }

    [Fact]
    public async Task InspectAsync_NoSuchServiceStderr_ReturnsServiceNotFound()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("inspect-no-such-service", """
          #!/bin/sh
          printf 'no such service: missing\n' >&2
          exit 1
          """);
      var driver = new DockerCliServiceDriver(fixture.Resolver);
      driver.Initialize(Context());

      var response = await driver.InspectAsync(
          Context(),
          "missing",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Service.NotFound, response.ErrorCode);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_SplitsCrCrLfAndLfLines()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var entries = await ReadStreamEntriesAsync("stream-line-endings", """
          #!/bin/sh
          printf 'cr\rcrlf\r\nlf\n'
          """);

      Assert.Equal(3, entries.Count);
      Assert.Equal("cr", entries[0].Line);
      Assert.Equal("crlf", entries[1].Line);
      Assert.Equal("lf", entries[2].Line);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_ReadsLineSplitAcrossBuffers()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var entries = await ReadStreamEntriesAsync("stream-cross-buffer-line", """
          #!/bin/sh
          head -c 9000 /dev/zero | tr '\000' x
          printf '\n'
          """);

      var entry = Assert.Single(entries);
      Assert.Equal(new string('x', 9000), entry.Line);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_TruncatesOverCapLineAndKeepsEofLine()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var entries = await ReadStreamEntriesAsync("stream-truncated-line", """
          #!/bin/sh
          head -c 1048577 /dev/zero | tr '\000' x
          """);

      var marker = "…[line truncated at 1,048,576 chars]";
      var entry = Assert.Single(entries);
      Assert.Equal(1048576 + marker.Length, entry.Line.Length);
      Assert.Equal(marker, entry.Line[^marker.Length..]);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_ReturnsEofLineWithoutTrailingNewline()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var entries = await ReadStreamEntriesAsync("stream-eof-line", """
          #!/bin/sh
          printf 'last'
          """);

      var entry = Assert.Single(entries);
      Assert.Equal("last", entry.Line);
    }

    [Fact]
    public async Task VolumeCreate_NullOperationContextUsesComponentVerifyTls()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("context-component-verify-tls", CaptureArgsScript());
      var driver = new DockerCliVolumeDriver(fixture.Resolver);
      driver.Initialize(new DriverContext("docker")
      {
        CertificatePath = "/certs",
        VerifyTls = true
      });

      var response = await driver.CreateAsync(
          null!,
          new VolumeCreateConfig { Name = "v" },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = File.ReadAllText(fixture.BinaryPath + ".args");
      Assert.Contains("<--tlsverify>", args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VolumeCreate_OperationVerifyTlsFalseOverridesComponentVerifyTls()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("context-operation-no-verify-tls", CaptureArgsScript());
      var driver = new DockerCliVolumeDriver(fixture.Resolver);
      driver.Initialize(new DriverContext("docker")
      {
        CertificatePath = "/certs",
        VerifyTls = true
      });

      var response = await driver.CreateAsync(
          new DriverContext("docker") { VerifyTls = false },
          new VolumeCreateConfig { Name = "v" },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = File.ReadAllText(fixture.BinaryPath + ".args");
      Assert.Contains("<--tls>", args, StringComparison.Ordinal);
      Assert.DoesNotContain("<--tlsverify>", args, StringComparison.Ordinal);
    }

    private static DriverContext Context() => new("docker");

    private static async Task<List<LogEntry>> ReadStreamEntriesAsync(string name, string script)
    {
      var fixture = CreateResolver(name, script);
      var driver = new DockerCliStreamDriver(fixture.Resolver);
      driver.Initialize(Context());

      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(
          Context(),
          "container",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
        entries.Add(entry);
      return entries;
    }

    private static string CaptureArgsScript() => """
        #!/bin/sh
        : > "$0.args"
        for arg in "$@"; do
          printf '<%s>\n' "$arg" >> "$0.args"
        done
        printf 'v\n'
        """;

    private static BinaryFixture CreateResolver(string name, string script)
    {
      var fixture = CreateMissingResolver(name);
      File.WriteAllText(fixture.BinaryPath, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            fixture.BinaryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      return fixture;
    }

    private static BinaryFixture CreateMissingResolver(string name)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", "prod-ready-docker-cli", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "docker");
      return new BinaryFixture(new DockerResolver(dir), path);
    }

    private sealed record BinaryFixture(IBinaryResolver Resolver, string BinaryPath);

    private sealed class DockerResolver(string directory) : IBinaryResolver
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
