using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  // ---- B-M4: WithContextSize must reject non-positive token counts (all three builders) --------
  public partial class BuilderModelExtensionsTests
  {
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RunnerBuilder_WithContextSize_RejectsNonPositiveValues(int tokens)
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel).UseModelRunner();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => runnerBuilder.WithContextSize(tokens));
        Assert.Equal("tokens", ex.ParamName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunnerBuilder_WithContextSize_AcceptsPositiveValue()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel).UseModelRunner();

        var result = runnerBuilder.WithContextSize(4096);

        Assert.Same(runnerBuilder, result);
      }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ServiceBuilder_WithContextSize_RejectsNonPositiveValues(int tokens)
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var serviceBuilder = new Builder().WithinDriver("docker", kernel).UseModel("ai/smollm2");

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => serviceBuilder.WithContextSize(tokens));
        Assert.Equal("tokens", ex.ParamName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ServiceBuilder_WithContextSize_AcceptsPositiveValue()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var serviceBuilder = new Builder().WithinDriver("docker", kernel).UseModel("ai/smollm2");

        var result = serviceBuilder.WithContextSize(4096);

        Assert.Same(serviceBuilder, result);
      }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    public void ModelRunOptionsBuilder_WithContextSize_RejectsNonPositiveValues(int tokens)
    {
      var builder = new ModelRunOptionsBuilder();

      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithContextSize(tokens));
      Assert.Equal("tokens", ex.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ModelRunOptionsBuilder_WithContextSize_AcceptsPositiveValue()
    {
      var builder = new ModelRunOptionsBuilder();

      var result = builder.WithContextSize(4096);

      Assert.Same(builder, result);
    }
  }
}
