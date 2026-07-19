using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public class BuilderContainerStartupBudgetTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task BuildAsync_UsesWaitTimeoutForContainerStartupBudget()
    {
      var starting = CommandResponse<Container>.Ok(new Container
      {
        Id = "container-123",
        State = new ContainerState { Running = false, Status = "created" }
      });
      var running = CommandResponse<Container>.Ok(new Container
      {
        Id = "container-123",
        State = new ContainerState { Running = true, Status = "running" }
      });
      var sequence = MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()));
      for (var i = 0; i < 31; i++)
        sequence = sequence.ReturnsAsync(starting);
      sequence.ReturnsAsync(running);
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerRemove()
          .SetupContainerGetLogs("ready");

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(1)
              .WaitForLogMessage("ready", 5000))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.Containers);
    }
  }
}
