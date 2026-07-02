using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
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
  }
}
