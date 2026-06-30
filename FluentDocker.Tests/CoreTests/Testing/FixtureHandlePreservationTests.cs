using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  /// <summary>
  /// Regression tests for the P0 fixture-teardown bug: a failing cleanup must
  /// leave the public <c>Resource</c>/<c>Kernel</c> handles available (for
  /// <c>LastTeardownDiagnostics</c>, retry, or manual cleanup) and only clear
  /// them after disposal succeeds. The old code nulled the handles in a
  /// <c>finally</c>, losing them even when teardown threw.
  /// </summary>
  [Trait("Category", "Unit")]
  public class FixtureHandlePreservationTests
  {
    [Fact]
    public async Task XunitResourceFixture_FailedTeardown_KeepsHandles_ThenClearsOnSuccess()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      var resource = new ThrowOnceResource();

      var fixture = new XunitResourceFixture<ThrowOnceResource>();
      await fixture.InitializeAsync(
          _ => resource,
          kernelFactory: () => Task.FromResult(kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      // First dispose: resource teardown throws and must propagate.
      await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DisposeAsync().AsTask());

      // Handles must remain accessible for diagnostics / retry — NOT nulled.
      Assert.Same(resource, fixture.Resource);
      Assert.Same(kernel, fixture.Kernel);

      // Second dispose: teardown succeeds, so handles are cleared.
      await fixture.DisposeAsync();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public async Task XunitContainerFixtureBase_FailedTeardown_PreservesHandlesAndDiagnostics()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop();

      // RemoveAsync fails the graceful teardown. With ForceRemoveOnDispose disabled,
      // ResourceBase records LastTeardownDiagnostics and rethrows.
      mockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("remove failed"));

      var fixture = new TestContainerFixture(() => Task.FromResult(kernel));
      await fixture.InitializeAsync();

      var resource = fixture.Resource;
      Assert.True(resource.IsInitialized);

      // Dispose: teardown throws and propagates.
      await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DisposeAsync().AsTask());

      // The bug fix: the typed-base fixture no longer nulls its handles in a finally,
      // so after a failed teardown Resource/Kernel and the captured teardown
      // diagnostics remain reachable for inspection and manual cleanup (e.g. via a
      // fresh kernel using resource.ResourceName).
      Assert.Same(resource, fixture.Resource);
      Assert.Same(kernel, fixture.Kernel);
      Assert.NotNull(resource.LastTeardownDiagnostics);
      Assert.NotNull(resource.LastTeardownDiagnostics!.TeardownException);

      // NOTE: a kernel-dependent retry is NOT attempted here; this test only proves
      // fixture handles and diagnostics remain visible after failed teardown.
    }

    /// <summary>
    /// Minimal <see cref="ITestResource"/> whose <see cref="DisposeAsync"/>
    /// throws on the first call and succeeds afterwards — the lightest way to
    /// drive a throwing teardown through the generic fixture.
    /// </summary>
    private sealed class ThrowOnceResource : ITestResource
    {
      private int _disposeCalls;

      public bool IsInitialized { get; private set; }

      public Task InitializeAsync(CancellationToken cancellationToken = default)
      {
        IsInitialized = true;
        return Task.CompletedTask;
      }

      public ValueTask DisposeAsync()
      {
        if (Interlocked.Increment(ref _disposeCalls) == 1)
          throw new InvalidOperationException("teardown failed");

        IsInitialized = false;
        return ValueTask.CompletedTask;
      }
    }

    private sealed class TestContainerFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitContainerFixtureBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory = kernelFactory;

      protected override void ConfigureContainer(IContainerBuilder builder)
          => builder.UseImage("alpine:latest");

      protected override Func<Task<FluentDockerKernel>> KernelFactory => _kernelFactory;

      protected override DockerResourceOptions? GetOptions()
          => new() { ForceRemoveOnDispose = false };
    }
  }
}
