using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliStreamDriver"/>: stderr filtering/marking
  /// of streamed logs, stdin honouring plus rejection of unsupported stream suppression on attach,
  /// and warning-level logging of malformed event lines.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliStreamDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task StreamLogsAsync_FiltersAndMarksStderr()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo out
echo err >&2
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var stdoutOnly = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("docker"),
          "ctr",
          new StreamLogsConfig { Follow = false, Stderr = false },
          TestContext.Current.CancellationToken))
        stdoutOnly.Add(line);

      var entries = new List<LogEntry>();
      await foreach (var entry in ((IStreamDriver)driver).StreamLogEntriesAsync(
          new DriverContext("docker"),
          "ctr",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
        entries.Add(entry);

      Assert.Equal(["out"], stdoutOnly);
      Assert.Contains(entries, e => e.Source == LogStreamSource.Stdout && e.Line == "out");
      Assert.Contains(entries, e => e.Source == LogStreamSource.Stderr && e.Line == "err");
    }

    [Fact]
    public async Task AttachAsync_HonorsStdinAndRejectsUnsupportedStreamSuppression()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"attach-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker($"""
#!/bin/sh
printf '%s\n' "$@" > '{record}'
sleep 2
""")));
      driver.Initialize(new DriverContext("docker"));

      await using var attach = (await driver.AttachAsync(
          new DriverContext("docker"),
          "ctr",
          new AttachConfig { Stdin = false },
          TestContext.Current.CancellationToken)).Data;
      await WaitForFileAsync(record);
      Assert.Contains("--no-stdin", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var unsupported = await driver.AttachAsync(
          new DriverContext("docker"),
          "ctr",
          new AttachConfig { NoStdout = true },
          TestContext.Current.CancellationToken);
      Assert.False(unsupported.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, unsupported.ErrorCode);
    }

    [Fact]
    public async Task StreamDrivers_LogMalformedLinesAtWarning()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var sink = new LevelLoggerProvider();
      using var factory = new LevelLoggerFactory(sink);
      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' 'not-json'
""")));
      driver.Initialize(new DriverContext("docker") { LoggerFactory = factory });

      await foreach (var _ in driver.StreamEventsAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      Assert.Contains(sink.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed class LevelLoggerFactory(LevelLoggerProvider provider) : ILoggerFactory
    {
      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

      public void Dispose()
      {
      }
    }

    private sealed class LevelLoggerProvider : ILoggerProvider
    {
      public ConcurrentBag<(LogLevel Level, string Message)> Entries { get; } = [];

      public ILogger CreateLogger(string categoryName) => new LevelLogger(Entries);

      public void Dispose()
      {
      }
    }

    private sealed class LevelLogger(ConcurrentBag<(LogLevel Level, string Message)> entries) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => LevelNullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception exception,
          Func<TState, Exception, string> formatter) =>
        entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class LevelNullScope : IDisposable
    {
      public static readonly LevelNullScope Instance = new();

      public void Dispose()
      {
      }
    }
  }
}
