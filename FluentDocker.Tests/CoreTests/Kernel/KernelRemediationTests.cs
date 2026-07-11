using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public class KernelRemediationTests
  {
    [Fact]
    public async Task BuildAsync_CalledTwice_WithBuiltInPackFactory_Throws()
    {
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerApi("api", d => d.AsDefault());

      await using var first = await builder.BuildAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.NotNull(first.SysCtl<IContainerDriver>("api"));
    }

    [Fact]
    public async Task BuildAsync_AfterFailedBuild_RejectsBuilderReuse()
    {
      var failOnce = new FailOnceDriverPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerApi("api", d => d.AsDefault())
          .WithDriver("custom", d => d.UseCustomDriverPack(failOnce));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_DuringDriverRegistration_DoesNotWaitForSlowInitialize()
    {
      var initializeStarted = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var completeInitialize = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var disposed = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var driver = new Mock<IDriver>();
      driver.SetupGet(d => d.Type).Returns(DriverType.DockerCli);
      driver.SetupGet(d => d.Runtime).Returns(RuntimeType.Docker);
      driver
          .Setup(d => d.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            initializeStarted.SetResult();
            await completeInitialize.Task.ConfigureAwait(false);
          });
      driver.As<IAsyncDisposable>()
          .Setup(d => d.DisposeAsync())
          .Callback(() => disposed.SetResult())
          .Returns(ValueTask.CompletedTask);
      var registry = new DriverRegistry(NullLoggerFactory.Instance);

      var register = registry.RegisterAsync(
          "slow", driver.Object, new DriverContext("slow"),
          TestContext.Current.CancellationToken);
      await initializeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

      var dispose = registry.DisposeAsync().AsTask();
      await dispose.WaitAsync(TestContext.Current.CancellationToken);

      completeInitialize.SetResult();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => register);
      await disposed.Task.WaitAsync(TestContext.Current.CancellationToken);
      driver.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await registry.DisposeAsync();
      var driver = new Mock<IDriver>();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          registry.RegisterAsync(
              "late", driver.Object, new DriverContext("late"),
              TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unregister_DisposesRemovedPack_AndClearsDefaultDriver()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new Mock<IDriverPack>();
      pack.SetupGet(p => p.Type).Returns(DriverType.DockerCli);
      pack.SetupGet(p => p.Runtime).Returns(RuntimeType.Docker);
      pack
          .Setup(p => p.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      pack.As<IAsyncDisposable>()
          .Setup(p => p.DisposeAsync())
          .Returns(ValueTask.CompletedTask);
      await registry.RegisterDriverPackAsync(
          "default", pack.Object, new DriverContext("default"),
          TestContext.Current.CancellationToken);

      registry.Unregister("default");

      pack.As<IAsyncDisposable>().Verify(p => p.DisposeAsync(), Times.Once);
      Assert.False(registry.IsRegistered("default"));
      Assert.Null(registry.GetDefaultDriverId());
    }

    [Fact]
    public async Task Unregister_DuringDispose_DisposesPackOnlyOnce()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new BlockingDisposePack();
      await registry.RegisterDriverPackAsync(
          "docker", pack, new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      var dispose = registry.DisposeAsync().AsTask();
      await pack.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

      Assert.Throws<ObjectDisposedException>(() => registry.Unregister("docker"));
      pack.CompleteDispose.SetResult();
      await dispose.WaitAsync(TestContext.Current.CancellationToken);

      Assert.Equal(1, pack.DisposeCount);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackReturnsWrongType_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "bad", new WrongTypeDriverPack(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IContainerDriver>("bad", out var instance);

      Assert.False(found);
      Assert.Null(instance);
      Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));
    }

    [Fact]
    public async Task TrySysCtl_WhenPackFallbackReturnsWrongType_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "bad", new WrongTypeFallbackDriverPack(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IContainerDriver>("bad", out var instance);

      Assert.False(found);
      Assert.Null(instance);
      Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));
    }

    [Fact]
    public async Task SysCtl_WhenPackReturnsWrongType_MessageNamesActualAndRequestedTypesAndLogsWarning()
    {
      // K-M4: TryResolve returning true with a non-assignable instance (a mis-mapped pack, e.g.
      // Drivers[typeof(IContainerDriver)] = imageDriver) must be diagnosable — a Warning-level log
      // line and an exception message that names both the actual and requested types, not a
      // cause-less "does not implement" that hides what actually happened.
      var factory = new RecordingLoggerFactory();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), factory);
      await kernel.RegisterDriverPackAsync(
          "bad", new WrongTypeDriverPack(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));

      Assert.Contains("String", ex.Message, StringComparison.Ordinal);
      Assert.Contains("IContainerDriver", ex.Message, StringComparison.Ordinal);
      Assert.Contains("not assignable", ex.Message, StringComparison.Ordinal);
      Assert.Contains(factory.Records, r =>
          r.Level == LogLevel.Warning && r.Message.Contains("String", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SysCtl_WhenDriverResolverReturnsWrongType_MessageNamesActualAndRequestedTypesAndLogsWarning()
    {
      // K-M4 plain-driver path (FluentDockerKernel.cs:~338-339): previously logged nothing at all.
      var factory = new RecordingLoggerFactory();
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await using var kernel = new FluentDockerKernel(registry, factory);
      await kernel.RegisterDriverAsync(
          "bad", new WrongTypeResolverDriver(), new DriverContext("bad"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.SysCtl<IContainerDriver>("bad"));

      Assert.Contains("String", ex.Message, StringComparison.Ordinal);
      Assert.Contains("IContainerDriver", ex.Message, StringComparison.Ordinal);
      Assert.Contains("not assignable", ex.Message, StringComparison.Ordinal);
      Assert.Contains(factory.Records, r =>
          r.Level == LogLevel.Warning && r.Message.Contains("String", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterDriverPackAsync_WhenInitializeThrows_DisposesFailingPack()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new Mock<IDriverPack>();
      pack
          .Setup(p => p.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("boom"));
      pack.As<IAsyncDisposable>()
          .Setup(p => p.DisposeAsync())
          .Returns(ValueTask.CompletedTask);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          registry.RegisterDriverPackAsync(
              "bad", pack.Object, new DriverContext("bad"),
              TestContext.Current.CancellationToken));

      pack.As<IAsyncDisposable>().Verify(p => p.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenInitializeThrows_DisposesFailingDriver()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var driver = new Mock<IDriver>();
      driver
          .Setup(d => d.InitializeAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("boom"));
      driver.As<IAsyncDisposable>()
          .Setup(d => d.DisposeAsync())
          .Returns(ValueTask.CompletedTask);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          registry.RegisterAsync(
              "bad", driver.Object, new DriverContext("bad"),
              TestContext.Current.CancellationToken));

      driver.As<IAsyncDisposable>().Verify(d => d.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void SysCtl_WithNullArguments_ThrowsClearDefaultDriverError()
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      var driverId = Assert.Throws<InvalidOperationException>(() =>
          kernel.SysCtl(null!, typeof(IContainerDriver)));
      var interfaceType = Assert.Throws<ArgumentNullException>(() =>
          kernel.SysCtl("driver", null!));

      Assert.Contains("No default driver configured", driverId.Message, StringComparison.Ordinal);
      Assert.Equal("interfaceType", interfaceType.ParamName);
    }

    private class FailOnceDriverPack : IDriverPack
    {
      private int _initializationCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public virtual Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default)
      {
        if (Interlocked.Increment(ref _initializationCount) == 1)
          throw new InvalidOperationException("first initialization fails");
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public T SysCtl<T>(string driverId) where T : class =>
          throw new InterfaceNotSupportedException(driverId, typeof(T).Name);

      public virtual object SysCtl(string driverId, Type interfaceType) =>
          throw new InterfaceNotSupportedException(driverId, interfaceType.Name);

      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        instance = null!;
        return false;
      }

      public virtual bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public System.Collections.Generic.IReadOnlyCollection<Type> GetSupportedInterfaces() =>
          [];
    }

    private sealed class WrongTypeDriverPack : FailOnceDriverPack
    {
      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = "not a container driver";
        return true;
      }
    }

    private sealed class WrongTypeFallbackDriverPack : FailOnceDriverPack
    {
      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public override bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public override object SysCtl(string driverId, Type interfaceType) =>
          "not a container driver";
    }

    private sealed class BlockingDisposePack : FailOnceDriverPack, IAsyncDisposable
    {
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public int DisposeCount => _disposeCount;

      private int _disposeCount;

      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public async ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref _disposeCount);
        DisposeStarted.SetResult();
        await CompleteDispose.Task.ConfigureAwait(false);
      }
    }

    /// <summary>
    /// Plain driver (not a pack) whose IDriverInterfaceResolver.TryResolve reports success but
    /// hands back an instance that does not implement the requested interface — the driver-path
    /// analog of WrongTypeDriverPack, for K-M4.
    /// </summary>
    private sealed class WrongTypeResolverDriver : IDriver, IDriverInterfaceResolver
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation)
      {
        implementation = "not a container driver";
        return true;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [typeof(IContainerDriver)];
    }

    /// <summary>
    /// Minimal in-memory ILoggerFactory for asserting on (Level, Message) pairs without extra
    /// test infra. Mirrors the pattern in LoggerFactoryWiringTests.cs.
    /// </summary>
    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
      public ConcurrentQueue<(LogLevel Level, string Message)> Records { get; } = new();
      public void AddProvider(ILoggerProvider provider) { }
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(Records);
      public void Dispose() { }

      private sealed class RecordingLogger(ConcurrentQueue<(LogLevel Level, string Message)> records) : ILogger
      {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            records.Enqueue((logLevel, formatter(state, exception)));
      }
    }
  }
}
