using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public class Chunk2KernelProdReadinessTests
  {
    [Fact]
    public async Task RegisterAsync_AfterContextDriverIdMismatch_RetryWithMatchingContextSucceeds()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var driver = new TestDriver();

      await Assert.ThrowsAsync<DriverContextIdMismatchException>(() =>
          registry.RegisterAsync(
              "docker", driver, new DriverContext("other"),
              TestContext.Current.CancellationToken));

      await registry.RegisterAsync(
          "docker", driver, new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      Assert.Same(driver, registry.GetDriver("docker"));
    }

    [Fact]
    public async Task RegisterDriverPackAsync_AfterContextDriverIdMismatch_RetryWithMatchingContextSucceeds()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new EmptyPack();

      await Assert.ThrowsAsync<DriverContextIdMismatchException>(() =>
          registry.RegisterDriverPackAsync(
              "docker", pack, new DriverContext("other"),
              TestContext.Current.CancellationToken));

      await registry.RegisterDriverPackAsync(
          "docker", pack, new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      Assert.Same(pack, registry.GetDriverPack("docker"));
    }

    [Fact]
    public async Task TrySysCtl_WhenPackSysCtlThrows_WrapsFaultAsInterfaceNotSupportedException()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "faulty", new FaultingPack(), new DriverContext("faulty"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
          kernel.TrySysCtl<IContainerDriver>("faulty", out _));

      Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public async Task BuildAsync_WhenMiddleDriverFails_DisposesUnreachedOwnedDriver()
    {
      var first = new TestDriver();
      var failing = new TestDriver(failInitialize: true);
      var unreached = new TestDriver();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("a", d => d.UseCustomDriver(first))
          .WithDriver("b", d => d.UseCustomDriver(failing))
          .WithDriver("c", d => d.UseCustomDriver(unreached));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.True(first.Disposed);
      Assert.True(failing.Disposed);
      Assert.True(unreached.Disposed);
    }

    [Fact]
    public async Task BuildAsync_WhenDuplicateDriverIdBeforeInitialize_DisposesJustAttemptedDriver()
    {
      var first = new TestDriver();
      var duplicate = new TestDriver();
      var unreached = new TestDriver();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("dup", d => d.UseCustomDriver(first))
          .WithDriver("dup", d => d.UseCustomDriver(duplicate))
          .WithDriver("c", d => d.UseCustomDriver(unreached));

      await Assert.ThrowsAsync<DriverException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.True(first.Disposed);
      Assert.True(duplicate.Disposed);
      Assert.True(unreached.Disposed);
    }

    [Fact]
    public async Task DriverNotFoundException_FromKernelIncludesRegisteredDriverIds()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "docker", new MockDriverPack(), new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverNotFoundException>(() =>
          kernel.SysCtl<IContainerDriver>("missing"));

      Assert.Contains("registered: [", ex.Message, StringComparison.Ordinal);
      Assert.Contains("docker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DriverNotFoundException_FromRegistryIncludesRegisteredDriverIds()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      await registry.RegisterAsync(
          "docker", new TestDriver(), new DriverContext("docker"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverNotFoundException>(() => registry.GetDriver("missing"));

      Assert.Contains("registered: [", ex.Message, StringComparison.Ordinal);
      Assert.Contains("docker", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachResultDisposeAsync_WhenInputDisposeThrows_StillDisposesOtherStreams()
    {
      var output = new TrackingStream();
      var error = new TrackingStream();
      var logger = new RecordingLogger();
      var result = new AttachResult
      {
        InputStream = new TrackingStream(new InvalidOperationException("input failed")),
        OutputStream = output,
        ErrorStream = error,
        Logger = logger
      };

      var ex = await Assert.ThrowsAsync<AggregateException>(() => result.DisposeAsync().AsTask());

      Assert.True(output.Disposed);
      Assert.True(error.Disposed);
      Assert.Contains(ex.InnerExceptions, inner => inner.Message == "input failed");
      Assert.Contains(logger.Warnings, message =>
          message.Contains("Attach stream disposal failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetDriver_WithNullDriverId_ReturnsDefaultDriver()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var driver = new TestDriver();
      await kernel.RegisterDriverAsync(
          "default", driver, new DriverContext("default"),
          TestContext.Current.CancellationToken);

      var resolved = kernel.GetDriver(null!);

      Assert.Same(driver, resolved);
    }

    [Fact]
    public async Task CapabilityChecks_WithNullDriverId_UseDefaultDriver()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var capabilities = DriverCapabilities.Default();
      capabilities.SupportsContainers = true;
      var driver = new TestDriver(capabilities: capabilities);
      await kernel.RegisterDriverAsync(
          "default", driver, new DriverContext("default"),
          TestContext.Current.CancellationToken);

      var resolved = await CapabilityChecks.GetCapabilitiesAsync(
          kernel, null!, TestContext.Current.CancellationToken);

      Assert.True(resolved.SupportsContainers);
    }

    private sealed class TestDriver : IDriver, IAsyncDisposable
    {
      private readonly bool _failInitialize;
      private readonly DriverCapabilities _capabilities;

      public TestDriver(bool failInitialize = false, DriverCapabilities? capabilities = null)
      {
        _failInitialize = failInitialize;
        _capabilities = capabilities ?? DriverCapabilities.Default();
      }

      public bool Disposed { get; private set; }
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default)
      {
        if (_failInitialize)
          throw new InvalidOperationException("initialize failed");
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(_capabilities);

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public ValueTask DisposeAsync()
      {
        Disposed = true;
        return ValueTask.CompletedTask;
      }
    }

    private class EmptyPack : IDriverPack
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
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

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
    }

    private sealed class FaultingPack : EmptyPack
    {
      public override object SysCtl(string driverId, Type interfaceType) =>
          throw new InvalidOperationException("pack fallback failed");
    }

    private sealed class TrackingStream : Stream
    {
      private readonly Exception _disposeException;

      public TrackingStream(Exception? disposeException = null) =>
          _disposeException = disposeException;

      public bool Disposed { get; private set; }
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => true;
      public override long Length => 0;
      public override long Position { get; set; }

      public override void Flush()
      {
      }

      public override int Read(byte[] buffer, int offset, int count) => 0;

      public override long Seek(long offset, SeekOrigin origin) =>
          throw new NotSupportedException();

      public override void SetLength(long value) =>
          throw new NotSupportedException();

      public override void Write(byte[] buffer, int offset, int count)
      {
      }

      protected override void Dispose(bool disposing)
      {
        Disposed = true;
        if (_disposeException != null)
          throw _disposeException;
        base.Dispose(disposing);
      }
    }

    private sealed class RecordingLogger : ILogger
    {
      public List<string> Warnings { get; } = [];

      public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
          NullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception exception,
          Func<TState, Exception, string> formatter)
      {
        if (logLevel == LogLevel.Warning)
          Warnings.Add(formatter(state, exception));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static NullScope Instance { get; } = new();

      public void Dispose()
      {
      }
    }
  }
}
