using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Tests.CoreTests.Service;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  [Collection(ModelEnvVarsCollection.Name)]
  public class DockerCliDriverPackModelEndpointTests
  {
    [Fact]
    public async Task InitializeAsync_InvalidDockerModelRunnerUrl_LogsWarningAndFallsBack()
    {
      var previous = Environment.GetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, "localhost:12434");
        var loggerFactory = new RecordingLoggerFactory();
        await using var pack = new DockerCliDriverPack();

        await pack.InitializeAsync(new DriverContext("docker") { LoggerFactory = loggerFactory },
            TestContext.Current.CancellationToken);

        Assert.Contains(loggerFactory.Records, r =>
            r.Level == LogLevel.Warning &&
            r.Message.Contains(ModelRunnerEndpoint.UrlEnvironmentVariable, StringComparison.Ordinal) &&
            r.Message.Contains("falling back", StringComparison.OrdinalIgnoreCase));
      }
      finally
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, previous);
      }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
      public ConcurrentQueue<LogRecord> Records { get; } = new();
      public void AddProvider(ILoggerProvider provider) { }
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Records);
      public void Dispose() { }
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogRecord> records) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
          Exception? exception, Func<TState, Exception?, string> formatter) =>
          records.Enqueue(new LogRecord(logLevel, category, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }

    private sealed record LogRecord(LogLevel Level, string Category, string Message);
  }
}
