using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ResourceInitializationExceptionTests
  {
    [Fact]
    public async Task CreateAndInitializeAsync_InitFailure_ThrowsDiagnosticsException()
    {
      var kernel = new TrackingKernel();
      var resource = new DiagnosticsFailureResource(kernel);

      var ex = await Assert.ThrowsAsync<ResourceInitializationException>(
          () => ResourceLifecycle.CreateAndInitializeAsync(
              _ => resource,
              () => Task.FromResult<FluentDockerKernel>(kernel),
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("Resource initialization failed.", ex.Message);
      Assert.IsType<InvalidOperationException>(ex.InnerException);
      Assert.Equal("provision failed", ex.InnerException.Message);
      Assert.NotNull(ex.Diagnostics);
      Assert.Equal("diagnostic logs", ex.Diagnostics.Logs);
      Assert.True(resource.TeardownWasCalled);
      Assert.True(kernel.DisposeWasCalled);
    }

    [Fact]
    public async Task InitializeAsync_InitFailure_CopiesContainerLogTailForCompat()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      await using (kernel.ConfigureAwait(false))
      {
        var resource = new DiagnosticsFailureResource(kernel, withLogTail: true);

        var ex = await Assert.ThrowsAsync<ResourceInitializationException>(
            () => resource.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal("tail from builder", ex.Data["ContainerLogTail"]);
        Assert.NotNull(ex.Diagnostics);
        Assert.Equal("tail from builder", ex.Diagnostics.Logs);
      }
    }

    [Fact]
    public async Task ToString_InitFailure_IncludesCollectedDiagnostics()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      await using (kernel.ConfigureAwait(false))
      {
        var resource = new DiagnosticsFailureResource(kernel);

        var ex = await Assert.ThrowsAsync<ResourceInitializationException>(
            () => resource.InitializeAsync(TestContext.Current.CancellationToken));

        var text = ex.ToString();
        Assert.Contains("Resource diagnostics:", text);
        Assert.Contains("diagnostic logs", text);
        Assert.Contains("provision failed", text);
      }
    }

    private sealed class DiagnosticsFailureResource(
        FluentDockerKernel kernel,
        bool withLogTail = false) : ResourceBase(kernel)
    {
      public bool TeardownWasCalled { get; private set; }

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ProvisionAsync(CancellationToken cancellationToken)
      {
        var ex = new InvalidOperationException("provision failed");
        if (withLogTail)
          ex.Data["ContainerLogTail"] = "tail from builder";
        return Task.FromException(ex);
      }

      protected override Task TeardownAsync(CancellationToken cancellationToken)
      {
        TeardownWasCalled = true;
        return Task.CompletedTask;
      }

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task<ResourceDiagnostics> CollectDiagnosticsAsync(
          Exception failure,
          CancellationToken cancellationToken = default)
          => Task.FromResult(new ResourceDiagnostics
          {
            Failure = failure,
            Logs = withLogTail ? (string)failure.Data["ContainerLogTail"]! : "diagnostic logs"
          });

    }

    private sealed class TrackingKernel()
        : FluentDockerKernel(
            new DriverRegistry(NullLoggerFactory.Instance),
            NullLoggerFactory.Instance)
    {
      public bool DisposeWasCalled { get; private set; }

      public override async ValueTask DisposeAsync()
      {
        DisposeWasCalled = true;
        await base.DisposeAsync().ConfigureAwait(false);
      }
    }
  }
}
