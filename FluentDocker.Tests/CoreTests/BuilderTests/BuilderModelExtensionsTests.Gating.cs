using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  public partial class BuilderModelExtensionsTests
  {
    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunnerBuilder_PullAndConfigure_HoldOneGate_NoInterleaving()
    {
      var model = ModelReference.Parse("ai/dmr7-" + Guid.NewGuid().ToString("N"));
      var events = new List<string>();
      Task interloper = Task.CompletedTask;
      var pack = new MockDriverPack()
          .SetupModelInspectMissing()
          .EnableModelDrivers();

      pack.ModelManagementDriver
          .Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()))
          .Returns<DriverContext, ModelReference, IProgress<ModelPullProgress>, CancellationToken>((_, m, _, ct) =>
          {
            Add(events, "pull");
            interloper = RecordWhenAcquiredAsync(ModelOperationGate.AcquireAsync(m, ct), events);
            return Task.FromResult(CommandResponse<ModelInfo>.Ok(new ModelInfo { Reference = m }));
          });
      pack.ModelRuntimeDriver
          .Setup(d => d.ConfigureAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<ModelConfigureOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            Add(events, "configure");
            return CommandResponse<Unit>.Ok(Unit.Default);
          });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel(model)
            .WithContextSize(4096)
            .PullIfMissing()
            .BuildAsync(TestContext.Current.CancellationToken);

        await interloper.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      }

      Assert.Equal(new[] { "pull", "configure", "interloper" }, events);
    }

    private static async Task RecordWhenAcquiredAsync(Task<IAsyncDisposable> acquire, List<string> events)
    {
      await using var gate = await acquire.ConfigureAwait(false);
      Add(events, "interloper");
    }

    private static void Add(List<string> events, string value)
    {
      lock (events)
        events.Add(value);
    }
  }
}
