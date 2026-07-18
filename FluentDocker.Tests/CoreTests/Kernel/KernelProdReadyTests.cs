#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Kernel
{
  public sealed class KernelProdReadyTests
  {
    [Trait("Category", "Unit")]
    [Fact]
    public async Task UnregisterAsync_WhenDriverDisposeNeverCompletes_AbandonsWithinBudget()
    {
      var registry = new ShortBudgetRegistry();
      var driver = new HangingDisposeDriver();
      await registry.RegisterAsync(
          "driver", driver, new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      var unregister = registry.UnregisterAsync(
          "driver", TestContext.Current.CancellationToken);

      try
      {
        var completed = await Task.WhenAny(
            unregister,
            Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Same(unregister, completed);
        await unregister;
        Assert.Equal(1, registry.AbandonedDriverCount);
        Assert.False(registry.IsRegistered("driver"));
      }
      finally
      {
        driver.CompleteDispose();
        await Task.WhenAny(unregister, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task UnregisterAsync_WhenCallerTokenCancelsDuringDispose_StillCompletesWithoutThrowing()
    {
      var registry = new ShortBudgetRegistry();
      var driver = new HangingDisposeDriver();
      await registry.RegisterAsync(
          "driver", driver, new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      using var cts = new CancellationTokenSource();
      var unregister = registry.UnregisterAsync("driver", cts.Token);
      // Removal has already committed synchronously; the parked task is now awaiting disposal.
      cts.Cancel();

      try
      {
        // Once removal commits, disposal runs on the budget only. A cancelled caller token must not
        // throw out of UnregisterAsync nor orphan the removed driver uncounted.
        await unregister;
        Assert.Equal(1, registry.AbandonedDriverCount);
        Assert.False(registry.IsRegistered("driver"));
      }
      finally
      {
        driver.CompleteDispose();
        await Task.WhenAny(unregister, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RegisterAsync_WhenInitializeThrowsAfterInitStarted_DisposesFailingDriverWithinBudget()
    {
      // K-M1: the rollback path in RegisterAsync's catch block must dispose the failed driver
      // through the same budgeted helper UnregisterAsync/DisposeAsync use, not the unbudgeted
      // DisposeDriverSafelyAsync — otherwise a driver whose DisposeAsync hangs blocks the
      // registration call (and its caller) forever.
      var registry = new ShortBudgetRegistry();
      var driver = new HangingDisposeDriver(failInitialize: true);

      var register = registry.RegisterAsync(
          "driver", driver, new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      try
      {
        var completed = await Task.WhenAny(
            register,
            Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Same(register, completed);
        await Assert.ThrowsAsync<OperationCanceledException>(() => register);
        Assert.Equal(1, registry.AbandonedDriverCount);
        Assert.False(registry.IsRegistered("driver"));
      }
      finally
      {
        driver.CompleteDispose();
        await Task.WhenAny(register, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task RegisterDriverPackAsync_WhenInitializeThrowsAfterInitStarted_DisposesFailingPackWithinBudget()
    {
      // K-M1 pack path: DriverRegistry.cs:300-311 has the same unbudgeted-rollback bug as the
      // driver path above, via DisposeDriverPackSafelyAsync.
      var registry = new ShortBudgetRegistry();
      var pack = new HangingDisposePack();

      var register = registry.RegisterDriverPackAsync(
          "pack", pack, new DriverContext("pack"),
          TestContext.Current.CancellationToken);

      try
      {
        var completed = await Task.WhenAny(
            register,
            Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Same(register, completed);
        await Assert.ThrowsAsync<OperationCanceledException>(() => register);
        Assert.Equal(1, registry.AbandonedDriverCount);
        Assert.False(registry.IsRegistered("pack"));
      }
      finally
      {
        pack.CompleteDispose();
        await Task.WhenAny(register, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        await registry.DisposeAsync();
      }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task SysCtl_WhenPackTryResolveThrowsUnexpected_WrapsDriverException()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "driver",
          new ThrowingTryResolvePack(new UnexpectedResolutionException()),
          new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverException>(() =>
          kernel.SysCtl<IContainerDriver>("driver"));

      Assert.IsType<UnexpectedResolutionException>(ex.InnerException);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task TrySysCtl_WhenPackTryResolveThrowsUnexpected_WrapsDriverException()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "driver",
          new ThrowingTryResolvePack(new UnexpectedResolutionException()),
          new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      var ex = Assert.Throws<DriverException>(() =>
          kernel.TrySysCtl<IContainerDriver>("driver", out _));

      Assert.IsType<UnexpectedResolutionException>(ex.InnerException);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public async Task TrySysCtl_WhenInterfaceUnsupported_ReturnsFalse()
    {
      await using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(
          "driver", new UnsupportedPack(), new DriverContext("driver"),
          TestContext.Current.CancellationToken);

      var resolved = kernel.TrySysCtl<IContainerDriver>("driver", out var driver);

      Assert.False(resolved);
      Assert.Null(driver);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DriverBuilders_WithNonPositiveTimeouts_ThrowArgumentOutOfRange()
    {
      Assert.Equal("timeout", Assert.Throws<ArgumentOutOfRangeException>(() =>
          FluentDockerKernel.Create(NullLoggerFactory.Instance)
              .WithDockerCli("docker", b => b.WithRequestTimeout(TimeSpan.Zero))).ParamName);

      Assert.Equal("timeout", Assert.Throws<ArgumentOutOfRangeException>(() =>
          FluentDockerKernel.Create(NullLoggerFactory.Instance)
              .WithDockerApi("api", b => b.WithConnectionTimeout(TimeSpan.Zero))).ParamName);

      Assert.Equal("timeout", Assert.Throws<ArgumentOutOfRangeException>(() =>
          FluentDockerKernel.Create(NullLoggerFactory.Instance)
              .WithPodmanCli("podman", b => b.WithRequestTimeout(TimeSpan.FromMilliseconds(-1)))).ParamName);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void BuildScope_WhenKernelNull_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() => new BuildScope(null!, "driver"));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void BuildScope_WhenDriverIdBlank_ThrowsArgumentException()
    {
      using var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      var ex = Assert.Throws<ArgumentException>(() => new BuildScope(kernel, " "));

      Assert.Equal("driverId", ex.ParamName);
    }

    private sealed class ShortBudgetRegistry : DriverRegistry
    {
      public ShortBudgetRegistry() : base(NullLoggerFactory.Instance)
      {
      }

      protected override TimeSpan DisposeBudget => TimeSpan.FromMilliseconds(25);
    }

    private sealed class HangingDisposeDriver(bool failInitialize = false) : IDriver, IAsyncDisposable
    {
      private readonly TaskCompletionSource _dispose =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          failInitialize
              ? throw new OperationCanceledException("simulated cancellation after init started")
              : Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public ValueTask DisposeAsync() => new(_dispose.Task);

      public void CompleteDispose()
      {
        _dispose.TrySetResult();
      }
    }

    private sealed class HangingDisposePack : IDriverPack, IAsyncDisposable
    {
      private readonly TaskCompletionSource _dispose =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          throw new OperationCanceledException("simulated cancellation after init started");

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation)
      {
        implementation = null;
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];

      public ValueTask DisposeAsync() => new(_dispose.Task);

      public void CompleteDispose()
      {
        _dispose.TrySetResult();
      }
    }

    private sealed class UnexpectedResolutionException : Exception
    {
    }

    private sealed class ThrowingTryResolvePack(Exception exception) : UnsupportedPack
    {
      public override bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation)
      {
        implementation = null;
        throw exception;
      }
    }

    private class UnsupportedPack : IDriverPack
    {
      public DriverType Type => DriverType.Custom;
      public RuntimeType Runtime => RuntimeType.Unknown;

      public Task InitializeAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          Task.CompletedTask;

      public Task<DriverCapabilities> GetCapabilitiesAsync(
          CancellationToken cancellationToken = default) =>
          Task.FromResult(DriverCapabilities.Default());

      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
          Task.FromResult(true);

      public virtual bool TryResolve(Type interfaceType, [NotNullWhen(true)] out object? implementation)
      {
        implementation = null;
        return false;
      }

      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
    }
  }
}
