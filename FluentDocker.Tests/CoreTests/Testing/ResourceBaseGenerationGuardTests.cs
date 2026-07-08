using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ResourceBaseGenerationGuardTests : IAsyncLifetime
  {
    private FluentDockerKernel _kernel = null!;

    public async ValueTask InitializeAsync()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      _kernel = kernel;
    }

    public async ValueTask DisposeAsync()
    {
      GC.SuppressFinalize(this);
      if (_kernel != null)
        await _kernel.DisposeAsync();
    }

    [Fact]
    public async Task LateProvisionCompletion_AfterReinitialize_DoesNotOverwriteFreshHandle()
    {
      var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var resource = new GenerationGuardResource(
          _kernel,
          new Queue<ProvisionStep>([
            new ProvisionStep("stale", firstEntered, firstRelease),
            new ProvisionStep("fresh", secondEntered, secondRelease)
          ]),
          new DockerResourceOptions
          {
            InitializationTimeout = TimeSpan.FromMilliseconds(20),
            TeardownTimeout = TimeSpan.FromMilliseconds(50)
          });

      var firstInit = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<ResourceInitializationException>(() => firstInit);

      await resource.DisposeAsync();

      var secondInit = resource.InitializeAsync(TestContext.Current.CancellationToken);
      await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      secondRelease.SetResult();
      await secondInit;
      Assert.Equal("fresh", resource.Handle);

      firstRelease.SetResult();
      await resource.StaleCommitObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

      Assert.Equal("fresh", resource.Handle);
      Assert.Equal("fresh", resource.ResourceName);
    }

    private sealed record ProvisionStep(
        string Name,
        TaskCompletionSource Entered,
        TaskCompletionSource Release);

    private sealed class GenerationGuardResource(
        FluentDockerKernel kernel,
        Queue<ProvisionStep> steps,
        DockerResourceOptions options) : ResourceBase(kernel, options)
    {
      public string Handle { get; private set; } = string.Empty;
      public TaskCompletionSource StaleCommitObserved { get; } =
          new(TaskCreationOptions.RunContinuationsAsynchronously);

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override async Task ProvisionAsync(CancellationToken cancellationToken)
      {
        var generation = ProvisionGeneration;
        var step = steps.Dequeue();
        step.Entered.SetResult();
        await step.Release.Task.ConfigureAwait(false);

        if (TryCommitProvision(generation, () =>
        {
          Handle = step.Name;
          ResourceName = step.Name;
        }))
        {
          return;
        }

        StaleCommitObserved.SetResult();
      }

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;
    }
  }
}
