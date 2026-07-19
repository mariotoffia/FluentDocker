using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  [Trait("Category", "Unit")]
  public sealed class KernelDriverPortsRemediationTests
  {
    [Fact]
    public async Task BuildAsync_WhenCanceledBeforeRegistryOwnership_LeavesUserPackIntact()
    {
      // KRN-MAJ-4: a user-supplied pack never taken into ownership (build canceled pre-registration)
      // is left intact for the caller to reuse or dispose — consistent with the registry's own
      // pre-ownership contract (see RegisterDriverPackAsync_WhenDriverIdDuplicate...).
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();
      var pack = new TrackingPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("custom", d => d.UseCustomDriverPack(pack));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          builder.BuildAsync(cts.Token));

      Assert.Equal(0, pack.DisposeCount);
    }

    [Fact]
    public async Task BuildAsync_WhenCanceledDuringInitialize_DisposesAttemptedPackOnce()
    {
      using var cts = new CancellationTokenSource();
      var pack = new CancelsDuringInitializePack(cts);
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("custom", d => d.UseCustomDriverPack(pack));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          builder.BuildAsync(cts.Token));

      Assert.Equal(1, pack.DisposeCount);
    }

    [Fact]
    public async Task RegisterDriverPackAsync_WhenDriverIdDuplicate_DoesNotDisposeSecondPackBeforeOwnership()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var first = new TrackingPack();
      var second = new TrackingPack();
      await registry.RegisterDriverPackAsync(
          "duplicate", first, new DriverContext("duplicate"),
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<DriverException>(() =>
          registry.RegisterDriverPackAsync(
              "duplicate", second, new DriverContext("duplicate"),
              TestContext.Current.CancellationToken));

      Assert.Equal(0, first.DisposeCount);
      Assert.Equal(0, second.DisposeCount);
      Assert.Same(first, registry.GetDriverPack("duplicate"));
      await registry.DisposeAsync();
      Assert.Equal(1, first.DisposeCount);
      Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public async Task SysCtl_WhenPackFallbackThrowsCustomException_WrapsCauseAsInnerException()
    {
      var cause = new PackFallbackException();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "faulty", new FaultingPack(cause), new DriverContext("faulty"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverException>(() =>
          kernel.SysCtl<IContainerDriver>("faulty"));

      Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackFallbackThrowsCustomException_WrapsCauseAsInnerException()
    {
      var cause = new PackFallbackException();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "faulty", new FaultingPack(cause), new DriverContext("faulty"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverException>(() =>
          kernel.TrySysCtl<IContainerDriver>("faulty", out _));

      Assert.Same(cause, ex.InnerException);
    }

    [Fact]
    public async Task TrySysCtl_WhenPackFallbackIsUnsupported_DoesNotLogWarning()
    {
      var loggerFactory = new RecordingLoggerFactory();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync(
          "unsupported", new TrackingPack(), new DriverContext("unsupported"),
          TestContext.Current.CancellationToken);

      var found = kernel.TrySysCtl<IContainerDriver>("unsupported", out _);

      Assert.False(found);
      Assert.DoesNotContain(loggerFactory.Entries, e => e.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task SysCtl_WhenPackFallbackThrowsCancellation_PropagatesOperationCanceledException()
    {
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "cancelled", new CancelledPack(cts.Token), new DriverContext("cancelled"),
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        kernel.SysCtl<IContainerDriver>("cancelled");
        await Task.CompletedTask;
      });
    }

    [Fact]
    public async Task RegisterDriverPackAsync_WhenContextDriverIdDiffers_ThrowsDedicatedException()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);

      var ex = await Assert.ThrowsAsync<DriverContextIdMismatchException>(() =>
          registry.RegisterDriverPackAsync(
              "actual", new TrackingPack(), new DriverContext("other"),
              TestContext.Current.CancellationToken));

      Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task EnsureCapabilityAsync_Model_WhenNotSupported_Throws()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverAsync(
          "driver", new CapabilityDriver(new DriverCapabilities { SupportsModels = false }),
          new DriverContext("driver"), TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<CapabilityNotSupportedException>(() =>
          kernel.EnsureCapabilityAsync(
              "driver", DriverCapability.Model, TestContext.Current.CancellationToken));

      Assert.Equal("Models", ex.CapabilityName);
    }

    [Fact]
    public void IDriverRegistry_RequiresAsyncDisposableImplementations()
    {
      Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IDriverRegistry)));
    }

    [Fact]
    public async Task DisposeAllAsync_WhenCleanupTokenCancels_SkipsStartingRemainingDisposals()
    {
      using var cts = new CancellationTokenSource();
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "driver");
      var notStarted = new CountingService(kernel);
      var blocking = new BlockingService(kernel);
      scope.AddResult(notStarted);
      scope.AddResult(blocking);

      var disposing = scope.DisposeAllAsync(cts.Token);
      await blocking.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
      await cts.CancelAsync();
      await disposing.WaitAsync(TestContext.Current.CancellationToken);
      blocking.CompleteDispose.SetResult();

      Assert.Equal(0, notStarted.DisposeAsyncCount);
    }

    [Fact]
    public async Task FirstPartyPacks_FormatGenericUnsupportedInterfaceNames()
    {
      // After KRN-MAJ-7 the pack has no driverId-based SysCtl; the kernel formats the unsupported
      // interface name when resolution (pack.TryResolve) finds nothing.
      foreach (var pack in new IDriverPack[] { new DockerCliDriverPack(), new DockerApiDriverPack() })
      {
        await using var kernel = new FluentDockerKernel(
            new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
        await kernel.RegisterDriverPackAsync(
            "driver", pack,
            new DriverContext("driver") { ModelRunnerEndpoint = ModelRunnerEndpoint.HostTcp() },
            TestContext.Current.CancellationToken);

        var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
            kernel.SysCtl<IGenericMissing<string>>("driver"));

        Assert.Contains("<", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("`", ex.Message, StringComparison.Ordinal);
      }
    }

    private class TrackingPack : IDriverPack, IAsyncDisposable
    {
      public int DisposeCount;
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;
      public virtual Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);
      public virtual object SysCtl(string driverId, Type interfaceType) =>
          throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      public virtual bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }
      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
      public ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeCount);
        return ValueTask.CompletedTask;
      }
    }

    // Faults now surface via TryResolve — the only pack-resolution path after the redundant
    // driverId-based SysCtl fallback was removed (KRN-MAJ-7); the kernel still wraps them as
    // DriverException (KRN-MAJ-1).
    private sealed class FaultingPack(Exception exception) : TrackingPack
    {
      public override bool TryResolve(Type interfaceType, out object implementation) => throw exception;
    }

    private sealed class CancelledPack(CancellationToken token) : TrackingPack
    {
      public override bool TryResolve(Type interfaceType, out object implementation) =>
          throw new OperationCanceledException(token);
    }

    private sealed class CancelsDuringInitializePack(CancellationTokenSource cts) : TrackingPack
    {
      public override Task InitializeAsync(
          DriverContext context,
          CancellationToken cancellationToken = default)
      {
        cts.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
      }
    }

    private sealed class CapabilityDriver(DriverCapabilities capabilities) : IDriver
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;
      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(capabilities);
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);
    }

    private class CountingService(FluentDockerKernel kernel) : IServiceAsync
    {
      public int DisposeAsyncCount;
      public string Name => "service";
      public ServiceRunningState State => ServiceRunningState.Stopped;
      public FluentDockerKernel Kernel { get; } = kernel;
      public string DriverId => "driver";
      public event ServiceDelegates.StateChange StateChange
      {
        add { }
        remove { }
      }
      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null!) =>
          this;
      public IServiceAsync RemoveHook(string uniqueName) => this;
      public virtual ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }
      public void Dispose()
      {
      }
    }

    private sealed class BlockingService(FluentDockerKernel kernel) : CountingService(kernel)
    {
      public TaskCompletionSource DisposeStarted { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);
      public TaskCompletionSource CompleteDispose { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CA2215 // Deliberate: the mock counts/controls dispose itself; calling base would double-count.
      public override async ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        DisposeStarted.SetResult();
        await CompleteDispose.Task.ConfigureAwait(false);
      }
#pragma warning restore CA2215
    }

    private sealed class PackFallbackException : Exception
    {
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
      public List<(LogLevel LogLevel, string Message)> Entries { get; } = [];

      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

      public void Dispose()
      {
      }
    }

    private sealed class RecordingLogger(List<(LogLevel LogLevel, string Message)> entries) : ILogger
    {
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
        entries.Add((logLevel, formatter(state, exception)));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static NullScope Instance { get; } = new();

      public void Dispose()
      {
      }
    }

    private interface IGenericMissing<T>
    {
    }
  }
}
