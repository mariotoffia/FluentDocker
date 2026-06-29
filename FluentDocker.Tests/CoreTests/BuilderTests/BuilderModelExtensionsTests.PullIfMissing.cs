using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
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
    public async Task RunnerBuilder_PullIfMissing_RethrowsInspectFailuresThatAreNotNotFound()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      pack.ModelManagementDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelInfo>.Fail("docker unavailable", ErrorCodes.Model.InspectFailed));
      pack.ModelManagementDriver
          .Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelInfo>.Ok(new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseModelRunner()
                .ForModel("ai/smollm2")
                .PullIfMissing()
                .BuildAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.Model.InspectFailed, ex.ErrorCode);
        pack.ModelManagementDriver.Verify(d => d.PullAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
      }
    }
  }
}
