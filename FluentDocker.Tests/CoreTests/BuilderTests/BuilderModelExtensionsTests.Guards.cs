using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  public partial class BuilderModelExtensionsTests
  {
    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModelRunner_AllowsInferenceOnlyPack()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.True(runner.Capabilities.SupportsInference);
        Assert.False(runner.Capabilities.SupportsRuntimeControl);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModel_Throws_WhenOnlyInferencePortPresent()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinDriver("docker", kernel).UseModel("ai/smollm2"));

        Assert.Equal(nameof(IModelRuntimeDriver), ex.InterfaceName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModelRunner_AllowsFullModelPack()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.True(runner.Capabilities.SupportsInference);
        Assert.True(runner.Capabilities.SupportsRuntimeControl);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModelRunner_AllowsRuntimeOnlyPack()
    {
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver<IModelRuntimeDriver>(pack.ModelRuntimeDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.False(runner.Capabilities.SupportsInference);
        Assert.True(runner.Capabilities.SupportsRuntimeControl);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModelRunner_AllowsManagementOnlyPack()
    {
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver<IModelManagementDriver>(pack.ModelManagementDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runnerBuilder = new Builder().WithinDriver("docker", kernel).UseModelRunner();

        Assert.NotNull(runnerBuilder);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScopedUseModelRunner_AllowsInferenceOnlyPack()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        IModelRunnerBuilder runnerBuilder = null;
        new Builder().WithinDriver("docker", kernel).UseContainer(container =>
            runnerBuilder = ((IDriverScopedBuilder)container).UseModelRunner().ForModel("ai/smollm2"));

        await using var runner = await runnerBuilder.BuildAsync(TestContext.Current.CancellationToken);
        Assert.True(runner.Capabilities.SupportsInference);
        Assert.False(runner.Capabilities.SupportsRuntimeControl);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryUseModelRunner_True_WhenInferenceOnlyPortPresent()
    {
      var pack = new MockDriverPack().EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        AssertTryUseModelRunner(kernel, expected: true);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryUseModelRunner_True_WhenRuntimeOnlyPortPresent()
    {
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver<IModelRuntimeDriver>(pack.ModelRuntimeDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        AssertTryUseModelRunner(kernel, expected: true);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryUseModelRunner_True_WhenManagementOnlyPortPresent()
    {
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver<IModelManagementDriver>(pack.ModelManagementDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        AssertTryUseModelRunner(kernel, expected: true);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryUseModelRunner_False_WhenNoModelPortsOnPlainMockPack()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        AssertTryUseModelRunner(kernel, expected: false);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModel_Succeeds_WhenRuntimePresent()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var service = await new Builder().WithinDriver("docker", kernel)
            .UseModel("ai/smollm2")
            .BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ai/smollm2:latest", service.Model.ToString());
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UseModel_ModelReferenceOverload_ThrowsRuntimeInterface_WhenOnlyInferencePortPresent()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinDriver("docker", kernel).UseModel(ModelReference.Parse("ai/smollm2")));

        Assert.Equal(nameof(IModelRuntimeDriver), ex.InterfaceName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScopedUseModel_ThrowsRuntimeInterface_WhenOnlyInferencePortPresent()
    {
      var pack = new MockDriverPack().SetupModelChat("ok").EnableModelInferenceOnly();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        InterfaceNotSupportedException caught = null;
        new Builder().WithinDriver("docker", kernel).UseContainer(container =>
        {
          caught = Assert.Throws<InterfaceNotSupportedException>(() =>
              ((IDriverScopedBuilder)container).UseModel("ai/smollm2"));
        });

        Assert.Equal(nameof(IModelRuntimeDriver), caught.InterfaceName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScopedUseModel_Succeeds_WhenRuntimePresent()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        IModelServiceBuilder serviceBuilder = null;
        new Builder().WithinDriver("docker", kernel).UseContainer(container =>
            serviceBuilder = ((IDriverScopedBuilder)container).UseModel("ai/smollm2"));

        await using var service = await serviceBuilder.BuildAsync(TestContext.Current.CancellationToken);
        Assert.Equal("ai/smollm2:latest", service.Model.ToString());
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TopLevelUseModelRunner_Throws_WhenNoModelPorts()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinDriver("docker", kernel).UseModelRunner());

        Assert.Equal(nameof(IModelRunnerBuilder), ex.InterfaceName);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TopLevelUseModel_ThrowsRuntimeInterface_WhenNoModelPorts()
    {
      var pack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var ex = Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinDriver("docker", kernel).UseModel("ai/smollm2"));

        Assert.Equal(nameof(IModelRuntimeDriver), ex.InterfaceName);
      }
    }

    private static void AssertTryUseModelRunner(
        FluentDocker.Kernel.FluentDockerKernel kernel, bool expected)
    {
      new Builder().WithinDriver("docker", kernel).UseContainer(container =>
      {
        var actual = ((IDriverScopedBuilder)container).TryUseModelRunner(out var runnerBuilder);
        Assert.Equal(expected, actual);
        Assert.Equal(expected, runnerBuilder != null);
      });
    }

    // ---- B5: Podman has no model support -------------------------------------

    [Fact]
    public async Task PodmanCliPack_Capabilities_NoModels()
    {
      var pack = new PodmanCliDriverPack();
      await pack.InitializeAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);
      var interfaces = pack.GetSupportedInterfaces();
      Assert.DoesNotContain(typeof(IModelManagementDriver), interfaces);
      Assert.DoesNotContain(typeof(IModelRuntimeDriver), interfaces);
      Assert.DoesNotContain(typeof(IModelInferenceDriver), interfaces);
    }
  }
}
