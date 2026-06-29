using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ModelResourceTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2:latest");

    [Fact]
    public async Task InitializeAsync_LoadsModel_AndExposesServiceRunnerModel()
    {
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        await resource.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(resource.IsInitialized);
        Assert.Same(resource.Service.Runner, resource.Runner);
        Assert.Equal(Model, resource.Model);
        pack.ModelRuntimeDriver.Verify(d => d.LoadAsync(
            It.IsAny<DriverContext>(), It.Is<ModelReference>(m => m.Equals(Model)),
            It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        await resource.DisposeAsync();
      }
    }

    [Fact]
    public async Task Properties_BeforeInitialize_Throw()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        var serviceError = Record.Exception(() => _ = resource.Service);
        var runnerError = Record.Exception(() => _ = resource.Runner);

        Assert.IsType<InvalidOperationException>(serviceError);
        Assert.IsType<InvalidOperationException>(runnerError);
        // Model is known from construction and must be readable before init.
        Assert.Equal(Model, resource.Model);
      }
    }

    [Fact]
    public async Task DisposeAsync_UnloadsModel_UnlessKeepRunning()
    {
      var unloadPack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var keepPack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var unloadKernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", unloadPack);
      var keepKernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", keepPack);
      await using (unloadKernel)
      await using (keepKernel)
      {
        var unloadResource = new ModelResource(unloadKernel, Model);
        var keepResource = new ModelResource(keepKernel, Model, b => b.KeepRunning());

        await unloadResource.InitializeAsync(TestContext.Current.CancellationToken);
        await unloadResource.DisposeAsync();
        await unloadResource.DisposeAsync();

        await keepResource.InitializeAsync(TestContext.Current.CancellationToken);
        await keepResource.DisposeAsync();

        unloadPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.Is<ModelReference>(m => m.Equals(Model)),
            It.IsAny<CancellationToken>()), Times.Once);
        keepPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Never);
      }
    }

    [Fact]
    public async Task InitializeAsync_LoadFailure_Throws()
    {
      var pack = new MockDriverPack()
          .SetupModelUnload()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("load failed", ErrorCodes.Model.LoadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        await Assert.ThrowsAsync<ModelRunnerException>(
            () => resource.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.False(resource.IsInitialized);
      }
    }
  }
}
