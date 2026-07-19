using System;
using System.Collections.Concurrent;
using System.Linq;
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
    public async Task InitializeAsync_InvalidDockerModelRunnerUrl_WarnsAndFallsBack()
    {
      // A malformed inference-only DOCKER_MODEL_RUNNER_URL must not break pack init for pure-container
      // workloads: init warns and falls back to host TCP, deferring any hard failure to actual inference
      // use (Default()/inference callers still fail fast via TryFromEnvironment).
      var previous = Environment.GetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, "localhost:12434");
        var factory = new RecordingLoggerFactory();
        await using var pack = new DockerCliDriverPack();

        await pack.InitializeAsync(
            new DriverContext("docker") { LoggerFactory = factory },
            TestContext.Current.CancellationToken);

        Assert.Contains(factory.Records, r =>
            r.Level == LogLevel.Warning &&
            r.Message.Contains(ModelRunnerEndpoint.UrlEnvironmentVariable, StringComparison.Ordinal));
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
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(Records);
      public void Dispose() { }
    }

    private sealed class RecordingLogger(ConcurrentQueue<LogRecord> records) : ILogger
    {
      private readonly ConcurrentQueue<LogRecord> _records = records;

      public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
          Exception? exception, Func<TState, Exception?, string> formatter)
        => _records.Enqueue(new LogRecord(logLevel, formatter(state, exception)));
    }

    private sealed record LogRecord(LogLevel Level, string Message);
  }
}
