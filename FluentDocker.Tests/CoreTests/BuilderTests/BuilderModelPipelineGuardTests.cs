using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderModelPipelineGuardTests
  {
    [Fact]
    public async Task UseModelRunner_AfterQueuedContainerOperation_ThrowsInvalidOperationException()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InvalidOperationException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine"))
            .UseModelRunner());

        Assert.Contains("cannot be chained after UseContainer", ex.Message);
      }
    }

    [Fact]
    public async Task UseModel_AfterQueuedContainerOperation_ThrowsInvalidOperationException()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InvalidOperationException>(() => new Builder()
            .WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("alpine"))
            .UseModel("ai/x"));

        Assert.Contains("cannot be chained after UseContainer", ex.Message);
      }
    }

    [Fact]
    public async Task UseModelRunner_WithoutQueuedOperations_ReturnsBuilder()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel).UseModelRunner();

        Assert.NotNull(runnerBuilder);
      }
    }
  }
}
