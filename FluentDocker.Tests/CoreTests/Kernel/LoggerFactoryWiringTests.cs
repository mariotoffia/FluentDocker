using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  /// <summary>
  /// Verifies that ILoggerFactory is required at the consumer entry points
  /// (KernelBuilder, FluentDockerKernel, DriverRegistry) and propagates through
  /// DriverContext to driver packs during InitializeAsync.
  /// </summary>
  [Trait("Category", "Unit")]
  public class LoggerFactoryWiringTests
  {
    [Fact]
    public void KernelBuilder_NullFactory_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new KernelBuilder(null!));
    }

    [Fact]
    public void FluentDockerKernel_NullFactory_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), null!));
    }

    [Fact]
    public void FluentDockerKernel_NullRegistry_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new FluentDockerKernel(null!, NullLoggerFactory.Instance));
    }

    [Fact]
    public void DriverRegistry_NullFactory_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new DriverRegistry(null!));
    }

    [Fact]
    public void FluentDockerKernel_Create_NullFactory_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => FluentDockerKernel.Create(null!));
    }

    [Fact]
    public async Task Kernel_ExposesFactoryPassedToBuilder()
    {
      var factory = (ILoggerFactory)NullLoggerFactory.Instance;

      using var kernel = await FluentDockerKernel.Create(factory)
          .WithDriver("probe", b => b.UseCustomDriverPack(new ProbeDriverPack()).AsDefault())
          .BuildAsync();

      Assert.Same(factory, kernel.LoggerFactory);
    }

    [Fact]
    public async Task Kernel_PropagatesFactoryToDriverContext()
    {
      var factory = new RecordingLoggerFactory();
      var probe = new ProbeDriverPack();

      using var kernel = await FluentDockerKernel.Create(factory)
          .WithDriver("probe", b => b.UseCustomDriverPack(probe).AsDefault())
          .BuildAsync();

      // The pack records the factory it received from DriverContext.LoggerFactory.
      Assert.Same(factory, probe.ObservedFactory);
    }

    [Fact]
    public async Task DriverRegistry_DisposalFailure_LogsWarning()
    {
      var factory = new RecordingLoggerFactory();
      var failing = new FailingDisposePack();

      var kernel = await FluentDockerKernel.Create(factory)
          .WithDriver("fail", b => b.UseCustomDriverPack(failing).AsDefault())
          .BuildAsync();

      await kernel.DisposeAsync();

      Assert.Contains(factory.Records, r =>
          r.Level == LogLevel.Warning &&
          r.Category == typeof(DriverRegistry).FullName &&
          r.Exception is InvalidOperationException);
    }

    /// <summary>
    /// Minimal in-memory ILoggerFactory used by these tests to assert on log entries
    /// without bringing in extra test infra. Records (Level, Category, Message, Exception).
    /// </summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
      public ConcurrentQueue<LogRecord> Records { get; } = new();
      public void AddProvider(ILoggerProvider provider) { /* no-op */ }
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Records);
      public void Dispose() { }
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<LoggerFactoryWiringTests.LogRecord> records) : ILogger
    {
      private readonly string _category = category;
      private readonly ConcurrentQueue<LogRecord> _records = records;

      public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
          Exception exception, Func<TState, Exception, string> formatter)
        => _records.Enqueue(new LogRecord(logLevel, _category, formatter(state, exception), exception));
    }

    private sealed record LogRecord(LogLevel Level, string Category, string Message, Exception Exception);

    private sealed class ProbeDriverPack : IDriverPack
    {
      public ILoggerFactory ObservedFactory { get; private set; }

      public DriverType Type => DriverType.DockerCli;
      public RuntimeType Runtime => RuntimeType.Docker;

      public Task InitializeAsync(DriverContext context, CancellationToken ct = default)
      {
        ObservedFactory = context.LoggerFactory;
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
          => Task.FromResult(new DriverCapabilities());

      public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class => null;
      public object SysCtl(string driverId, Type interfaceType) => null;
      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      { instance = null; return false; }
      public bool TryResolve(Type t, out object impl) { impl = null; return false; }
      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces()
          => [];
    }

    private sealed class FailingDisposePack : IDriverPack, IAsyncDisposable
    {
      public DriverType Type => DriverType.DockerCli;
      public RuntimeType Runtime => RuntimeType.Docker;

      public Task InitializeAsync(DriverContext context, CancellationToken ct = default)
          => Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
          => Task.FromResult(new DriverCapabilities());

      public Task<bool> IsHealthyAsync(CancellationToken ct = default) => Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class => null;
      public object SysCtl(string driverId, Type interfaceType) => null;
      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      { instance = null; return false; }
      public bool TryResolve(Type t, out object impl) { impl = null; return false; }
      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces()
          => [];

      public ValueTask DisposeAsync()
          => throw new InvalidOperationException("Simulated disposal failure");
    }
  }
}
