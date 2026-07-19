using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;
using FluentDocker.Model.Volumes;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerCli
{
  [Trait("Category", "Unit")]
  public class DockerCliChunk9ProdReadinessTests
  {
    [Fact]
    public async Task Chunk9_VolumeCreate_NullName_DoesNotPassEmptyQuotedPositionalArgument()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("volume-create-args", """
          #!/bin/sh
          : > "$0.args"
          for arg in "$@"; do
            printf '<%s>\n' "$arg" >> "$0.args"
          done
          printf 'generated-volume\n'
          """);
      var driver = CreateVolumeDriver(fixture.Resolver);

      var response = await driver.CreateAsync(
          Context(),
          new VolumeCreateConfig { Name = null! },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      Assert.DoesNotContain("<>", File.ReadAllLines(fixture.BinaryPath + ".args"));
    }

    [Fact]
    public async Task Chunk9_VolumeList_AllowsJsonOutputAboveLegacyFourMiBCap()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("volume-list-large", """
          #!/bin/sh
          printf '{"Name":"v","Driver":"local","Mountpoint":"'
          head -c 4194305 /dev/zero | tr '\000' a
          printf '","Labels":"","Scope":"local"}\n'
          """);
      var driver = CreateVolumeDriver(fixture.Resolver);

      var response = await driver.ListAsync(Context(), cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var volume = Assert.Single(response.Data!);
      Assert.Equal("v", volume.Name);
    }

    [Theory]
    [MemberData(nameof(InvalidImageSets))]
    public async Task Chunk9_ImageSave_NullOrEmptyImages_ReturnsInvalidArgumentFailure(string[] images)
    {
      var fixture = CreateResolver("image-save-invalid", "#!/bin/sh\nexit 0\n");
      var driver = CreateImageDriver(fixture.Resolver);

      var response = await driver.SaveAsync(
          Context(),
          images,
          "archive.tar",
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, response.ErrorCode);
      Assert.Contains("images", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chunk9_ImageSave_MissingBinaryReturnsDriverExceptionFailureWithPath()
    {
      var fixture = CreateMissingResolver("image-save-missing");
      var driver = CreateImageDriver(fixture.Resolver);

      var response = await driver.SaveAsync(
          Context(),
          ["alpine:latest"],
          "archive.tar",
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, response.ErrorCode);
      Assert.Contains(fixture.BinaryPath, response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chunk9_StreamEvents_MissingBinaryThrowsDriverExceptionWithPath()
    {
      var fixture = CreateMissingResolver("stream-events-missing");
      var driver = CreateStreamDriver(fixture.Resolver);

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamEventsAsync(Context(), cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
      Assert.Contains(fixture.BinaryPath, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chunk9_StreamLogs_MissingBinaryThrowsDriverExceptionWithPath()
    {
      var fixture = CreateMissingResolver("stream-logs-missing");
      var driver = CreateStreamDriver(fixture.Resolver);

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(Context(), "container", cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, ex.ErrorCode);
      Assert.Contains(fixture.BinaryPath, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chunk9_Attach_MissingBinaryReturnsDriverExceptionFailureWithPath()
    {
      var fixture = CreateMissingResolver("attach-missing");
      var loggerFactory = new CapturingLoggerFactory();
      var driver = new DockerCliStreamDriver(fixture.Resolver);
      driver.Initialize(new DriverContext("docker") { LoggerFactory = loggerFactory });

      var response = await driver.AttachAsync(
          Context(),
          "container",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, response.ErrorCode);
      Assert.Contains(fixture.BinaryPath, response.Error, StringComparison.Ordinal);
      Assert.DoesNotContain(
          loggerFactory.Logger.Entries,
          entry => entry.Level >= LogLevel.Warning
              && entry.Message.Contains("Process kill failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chunk9_Attach_ResultReceivesDriverLoggerForCleanupWarnings()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("attach-logger", "#!/bin/sh\nsleep 30\n");
      var loggerFactory = new CapturingLoggerFactory();
      var driver = new DockerCliStreamDriver(fixture.Resolver);
      driver.Initialize(new DriverContext("docker") { LoggerFactory = loggerFactory });

      var response = await driver.AttachAsync(
          Context(),
          "container",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      await using var attach = response.Data!;
      Assert.Same(loggerFactory.Logger, attach.Logger);
    }

    [Fact]
    public async Task Chunk9_VolumeCreate_OversizedStderrFormatsTruncationCapAsMiB()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var fixture = CreateResolver("volume-create-stderr-cap", """
          #!/bin/sh
          head -c 4194305 /dev/zero | tr '\000' e >&2
          exit 5
          """);
      var driver = CreateVolumeDriver(fixture.Resolver);

      var response = await driver.CreateAsync(
          Context(),
          new VolumeCreateConfig { Name = "v" },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("[stderr truncated at the 4 MiB cap]", response.Error, StringComparison.Ordinal);
      Assert.DoesNotContain("4194304-byte cap", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chunk9_VolumeCreate_MissingBinaryPreservesCommandExecutionFailedCode()
    {
      var fixture = CreateMissingResolver("volume-create-missing");
      var driver = CreateVolumeDriver(fixture.Resolver);

      var response = await driver.CreateAsync(
          Context(),
          new VolumeCreateConfig { Name = "v" },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, response.ErrorCode);
      Assert.Contains(fixture.BinaryPath, response.Error, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidImageSets()
    {
      yield return [null!];
      yield return [Array.Empty<string>()];
    }

    private static DriverContext Context() => new("docker");

    private static DockerCliVolumeDriver CreateVolumeDriver(IBinaryResolver resolver)
    {
      var driver = new DockerCliVolumeDriver(resolver);
      driver.Initialize(Context());
      return driver;
    }

    private static DockerCliImageDriver CreateImageDriver(IBinaryResolver resolver)
    {
      var driver = new DockerCliImageDriver(resolver);
      driver.Initialize(Context());
      return driver;
    }

    private static DockerCliStreamDriver CreateStreamDriver(IBinaryResolver resolver)
    {
      var driver = new DockerCliStreamDriver(resolver);
      driver.Initialize(Context());
      return driver;
    }

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
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", "chunk9", name, Guid.NewGuid().ToString("N"));
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

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
      public CapturingLogger Logger { get; } = new();
      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => Logger;
      public void Dispose()
      {
      }
    }

    private sealed class CapturingLogger : ILogger
    {
      public List<LogEntry> Entries { get; } = [];
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception? exception,
          Func<TState, Exception?, string> formatter)
      {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
      }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose()
      {
      }
    }
  }
}
