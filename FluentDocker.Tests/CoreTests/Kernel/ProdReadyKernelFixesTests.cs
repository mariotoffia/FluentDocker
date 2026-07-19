#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  public class ProdReadyKernelFixesTests
  {
    [Trait("Category", "Unit")]
    [Fact]
    public async Task RegisterDriverPackAsync_DuplicateSameInstanceRejected_KeepsOriginalRegistrationAlive()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new TrackingDriverPack();

      try
      {
        await registry.RegisterDriverPackAsync(
            "docker", pack, new DriverContext("docker"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DriverException>(() =>
            registry.RegisterDriverPackAsync(
                "docker", pack, new DriverContext("docker"),
                TestContext.Current.CancellationToken));

        Assert.Same(pack, registry.GetDriverPack("docker"));
        Assert.Equal(0, pack.DisposeAsyncCount);
        var capabilities = await registry.GetDriverPack("docker")
            .GetCapabilitiesAsync(TestContext.Current.CancellationToken);
        Assert.True(capabilities.SupportsContainers);
      }
      finally
      {
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RegisterDriverPackAsync_ContextIdMismatchThenRetrySameInstance_Succeeds()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new TrackingDriverPack();

      try
      {
        await Assert.ThrowsAsync<DriverContextIdMismatchException>(() =>
            registry.RegisterDriverPackAsync(
                "docker", pack, new DriverContext("other"),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, pack.InitializeCount);
        Assert.Equal(0, pack.DisposeAsyncCount);

        await registry.RegisterDriverPackAsync(
            "docker", pack, new DriverContext("docker"),
            TestContext.Current.CancellationToken);

        Assert.Same(pack, registry.GetDriverPack("docker"));
        Assert.Equal(1, pack.InitializeCount);
        Assert.Equal(0, pack.DisposeAsyncCount);
      }
      finally
      {
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RegisterDriverPackAsync_SameInstanceUnderSecondIdRejected_KeepsFirstRegistrationAlive()
    {
      var registry = new DriverRegistry(NullLoggerFactory.Instance);
      var pack = new TrackingDriverPack();

      try
      {
        await registry.RegisterDriverPackAsync(
            "primary", pack, new DriverContext("primary"),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DriverException>(() =>
            registry.RegisterDriverPackAsync(
                "secondary", pack, new DriverContext("secondary"),
                TestContext.Current.CancellationToken));

        Assert.True(registry.IsRegistered("primary"));
        Assert.False(registry.IsRegistered("secondary"));
        Assert.Same(pack, registry.GetDriverPack("primary"));
        Assert.Equal(0, pack.DisposeAsyncCount);
        var capabilities = await registry.GetDriverPack("primary")
            .GetCapabilitiesAsync(TestContext.Current.CancellationToken);
        Assert.True(capabilities.SupportsContainers);
      }
      finally
      {
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void BuildResults_ConstructorCopiesScopeList()
    {
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var scopes = new List<BuildScope> { scope };

      var results = new BuildResults(scopes);
      scopes.Clear();

      var resultScope = Assert.Single(results.Scopes);
      Assert.Same(scope, resultScope);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task BuildScope_DisposeAllAsync_WhenCancelled_RetainsServicesForRetry()
    {
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var scope = new BuildScope(kernel, "docker");
      var service = new TrackingService(kernel);
      scope.AddResult(service);
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await scope.DisposeAllAsync(cts.Token);

      var retained = Assert.Single(scope.Results);
      Assert.Same(service, retained);
      Assert.Equal(0, service.DisposeAsyncCount);

      await scope.DisposeAllAsync(TestContext.Current.CancellationToken);

      Assert.Empty(scope.Results);
      Assert.Equal(1, service.DisposeAsyncCount);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task BuildAsync_WhenSamePackRegisteredUnderTwoIds_DisposesSharedPackOnce()
    {
      var pack = new NonIdempotentCountingDriverPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("primary", d => d.UseCustomDriverPack(pack))
          .WithDriver("secondary", d => d.UseCustomDriverPack(pack));

      await Assert.ThrowsAsync<DriverException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.Equal(1, pack.DisposeAsyncCount);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task BuildAsync_WhenUnreachedConfigReusesRegisteredPack_DisposesSharedPackOnce()
    {
      var shared = new NonIdempotentCountingDriverPack();
      var failing = new FailingInitializeDriverPack();
      var builder = FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDriver("primary", d => d.UseCustomDriverPack(shared))
          .WithDriver("failing", d => d.UseCustomDriverPack(failing))
          .WithDriver("tail", d => d.UseCustomDriverPack(shared));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(TestContext.Current.CancellationToken));

      Assert.Equal(1, shared.DisposeAsyncCount);
      Assert.Equal(1, failing.DisposeAsyncCount);
    }

    private sealed class TrackingDriverPack : IDriverPack, IAsyncDisposable
    {
      private int _disposed;
      private bool _initialized;

      public int InitializeCount;
      public int DisposeAsyncCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default)
      {
        ThrowIfDisposed();
        Interlocked.Increment(ref InitializeCount);
        _initialized = true;
        return Task.CompletedTask;
      }

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default)
      {
        ThrowIfDisposed();
        return Task.FromResult(new DriverCapabilities { SupportsContainers = true });
      }

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
      {
        ThrowIfDisposed();
        return Task.FromResult(true);
      }

      public T SysCtl<T>(string driverId) where T : class
      {
        ThrowIfNotInitialized();
        throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
      }

      public object SysCtl(string driverId, Type interfaceType)
      {
        ThrowIfNotInitialized();
        throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      }

      public bool TrySysCtl<T>(string driverId, out T instance) where T : class
      {
        ThrowIfDisposed();
        ThrowIfNotInitialized();
        instance = null!;
        return false;
      }

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        ThrowIfDisposed();
        ThrowIfNotInitialized();
        implementation = null!;
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces()
      {
        ThrowIfDisposed();
        ThrowIfNotInitialized();
        return [];
      }

      public ValueTask DisposeAsync()
      {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
          Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }

      private void ThrowIfDisposed()
      {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
      }

      private void ThrowIfNotInitialized()
      {
        ThrowIfDisposed();
        if (!_initialized)
          throw new InvalidOperationException("TrackingDriverPack not initialized.");
      }
    }

    private class NonIdempotentCountingDriverPack : IDriverPack, IAsyncDisposable
    {
      public int DisposeAsyncCount;

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public virtual Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public bool TryResolve(Type interfaceType, out object implementation)
      {
        implementation = null!;
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];

      public ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }
    }

    private sealed class FailingInitializeDriverPack : NonIdempotentCountingDriverPack
    {
      public override Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          throw new InvalidOperationException("initialize failed");
    }

    private sealed class TrackingService(FluentDockerKernel kernel) : IServiceAsync
    {
      public int DisposeAsyncCount;
      public string Name => "service";
      public ServiceRunningState State => ServiceRunningState.Stopped;
      public FluentDockerKernel Kernel { get; } = kernel;
      public string DriverId => "docker";

      public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;
      public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string? uniqueName = null) =>
          this;
      public IServiceAsync RemoveHook(string? uniqueName) => this;

#pragma warning disable CS0067, CS8618
      public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067, CS8618

      public void Dispose()
      {
      }

      public ValueTask DisposeAsync()
      {
        Interlocked.Increment(ref DisposeAsyncCount);
        return ValueTask.CompletedTask;
      }
    }
  }
}
