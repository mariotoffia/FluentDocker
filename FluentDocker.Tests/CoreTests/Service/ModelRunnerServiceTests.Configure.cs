using System;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// MR2: <see cref="ModelRunnerService.ConfigureAsync"/> must run under the SAME per-model
  /// <see cref="ModelOperationGate"/> that build-time pull/load/unload use, so a concurrent gated
  /// op cannot stomp a model's persistent config. Uses test-unique model ids so the process-wide
  /// gate never collides with other (possibly parallel) tests.
  /// </summary>
  public partial class ModelRunnerServiceTests
  {
    [Fact]
    public async Task ConfigureAsync_SerializesOnPerModelGate_BlocksWhileHeld()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelConfigure());
      var model = ModelReference.Parse("ai/mr2-gate-same");

      await using (kernel)
      {
        // Hold the SAME per-model gate the service acquires, then start a configure for that model.
        var held = await ModelOperationGate.AcquireAsync(model, TestContext.Current.CancellationToken);
        Task configure;
        try
        {
          configure = runner.ConfigureAsync(
              model, new ModelConfigureOptions { ContextSize = 8192 }, TestContext.Current.CancellationToken);

          // While the gate is held, configure must NOT complete — it is serialized behind it.
          var winner = await Task.WhenAny(configure, Task.Delay(250, TestContext.Current.CancellationToken));
          Assert.NotSame(configure, winner);
          Assert.False(configure.IsCompleted);
        }
        finally
        {
          await held.DisposeAsync();
        }

        // Once the gate is released the previously-blocked configure proceeds to completion.
        await configure;
        Assert.True(configure.IsCompletedSuccessfully);
      }
    }

    [Fact]
    public async Task ConfigureAsync_DifferentModel_NotBlockedByHeldGate()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelConfigure());

      await using (kernel)
      {
        // The gate is keyed per model, so holding it for one model must not block another's configure.
        var held = await ModelOperationGate.AcquireAsync(
            ModelReference.Parse("ai/mr2-gate-held"), TestContext.Current.CancellationToken);
        try
        {
          var configure = runner.ConfigureAsync(
              ModelReference.Parse("ai/mr2-gate-other"), new ModelConfigureOptions { ContextSize = 4096 },
              TestContext.Current.CancellationToken);
          var winner = await Task.WhenAny(configure, Task.Delay(2000, TestContext.Current.CancellationToken));
          Assert.Same(configure, winner); // completed well within the timeout — not blocked
          await configure;
        }
        finally
        {
          await held.DisposeAsync();
        }
      }
    }

    [Fact]
    public async Task PullAsync_SameModel_SerializesOnPerModelGate()
    {
      var model = ModelReference.Parse("ai/dmr3-" + Guid.NewGuid().ToString("N"));
      var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      var calls = 0;
      var (kernel, runner) = await BuildAsync(p =>
      {
        p.ModelManagementDriver
            .Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
                It.IsAny<IProgress<ModelPullProgress>>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns<DriverContext, ModelReference, IProgress<ModelPullProgress>, System.Threading.CancellationToken>(
                async (_, m, _, _) =>
                {
                  var call = System.Threading.Interlocked.Increment(ref calls);
                  if (call == 1)
                  {
                    firstEntered.SetResult(true);
                    await releaseFirst.Task.ConfigureAwait(false);
                  }
                  return CommandResponse<ModelInfo>.Ok(new ModelInfo { Reference = m });
                });
      });

      await using (kernel)
      {
        var first = runner.PullAsync(model, null!, TestContext.Current.CancellationToken);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var second = runner.PullAsync(model, null!, TestContext.Current.CancellationToken);
        Assert.Equal(1, System.Threading.Volatile.Read(ref calls));
        Assert.False(second.IsCompleted);

        releaseFirst.SetResult(true);
        await first;
        await second;
        Assert.Equal(2, System.Threading.Volatile.Read(ref calls));
      }
    }

    [Fact]
    public async Task RemoveAsync_SerializesOnPerModelGate_BlocksWhileHeld()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelRemove());
      var model = ModelReference.Parse("ai/mr2-remove-gate-" + Guid.NewGuid().ToString("N"));

      await using (kernel)
      {
        var held = await ModelOperationGate.AcquireAsync(model, TestContext.Current.CancellationToken);
        Task remove;
        try
        {
          remove = runner.RemoveAsync(model, force: true, TestContext.Current.CancellationToken);

          var winner = await Task.WhenAny(remove, Task.Delay(250, TestContext.Current.CancellationToken));
          Assert.NotSame(remove, winner);
          Assert.False(remove.IsCompleted);
        }
        finally
        {
          await held.DisposeAsync();
        }

        await remove;
        Assert.True(remove.IsCompletedSuccessfully);
      }
    }
  }
}
