using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Minimal, dependency-free <see cref="ILoggerFactory"/> that writes <see cref="LogLevel.Warning"/>
  /// and above to <see cref="Console.Error"/>. It is the default kernel logger for test fixtures so
  /// that operational warnings a testing library must not hide — teardown-failed-but-recovered,
  /// session-label overlay skipped, swarm/kube label limitations, orphan-sweep errors — are visible
  /// instead of being dropped into a <see cref="NullLoggerFactory"/>. Set the environment variable
  /// <c>FLUENTDOCKER_TEST_LOG=off</c> to silence it, or supply your own kernel/logger to override.
  /// </summary>
  public sealed class DefaultFixtureLoggerFactory : ILoggerFactory
  {
    /// <summary>Shared instance used as the fixture default.</summary>
    public static readonly DefaultFixtureLoggerFactory Instance = new();

    private static readonly bool Silenced = string.Equals(
        Environment.GetEnvironmentVariable("FLUENTDOCKER_TEST_LOG"), "off", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        Silenced ? NullLogger.Instance : new ConsoleWarningLogger(categoryName);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class ConsoleWarningLogger(string category) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception? exception,
          Func<TState, Exception?, string> formatter)
      {
        if (!IsEnabled(logLevel))
          return;

        Console.Error.WriteLine($"[FluentDocker {logLevel}] {category}: {formatter(state, exception)}");
        if (exception != null)
          Console.Error.WriteLine(exception);
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();

      public void Dispose()
      {
      }
    }
  }
}
