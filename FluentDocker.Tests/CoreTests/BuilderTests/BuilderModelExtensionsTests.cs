using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the model builders, driver-scoped extensions, top-level
  /// shortcuts and driver-pack registration / capabilities (B1–B5).
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuilderModelExtensionsTests
  {
    private static async Task<FluentDocker.Kernel.FluentDockerKernel> MockKernelAsync(bool enableModels = true)
    {
      var pack = new MockDriverPack()
          .SetupModelPull(new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") })
          .SetupModelConfigure()
          .SetupModelLoad()
          .SetupModelUnload()
          .SetupModelChat("ok");
      if (enableModels)
        pack.EnableModelDrivers();

      return await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
    }

    // ---- B1: ModelRunnerBuilder ----------------------------------------------

    [Fact]
    public async Task RunnerBuilder_PullIfMissing_AndConfigure_AppliedAtBuild()
    {
      var pack = new MockDriverPack()
          .SetupModelPull(new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") })
          .SetupModelConfigure()
          .EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithContextSize(8192)
            .PullIfMissing()
            .BuildAsync(TestContext.Current.CancellationToken);

        await using (runner)
        {
          Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
        }

        pack.ModelManagementDriver.Verify(d => d.PullAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<System.IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        pack.ModelRuntimeDriver.Verify(d => d.ConfigureAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.Is<FluentDocker.Model.Models.Options.ModelConfigureOptions>(o => o.ContextSize == 8192),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task EndToEnd_BuilderToRunnerToChat()
    {
      var pack = new MockDriverPack().SetupModelChat("hi there").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var runner = new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .Build();

        await using ((System.IAsyncDisposable)runner)
        {
          var reply = await runner.ChatAsync("hello", TestContext.Current.CancellationToken);
          Assert.Equal("hi there", reply);
        }
      }
    }

    // ---- B2: ModelServiceBuilder ---------------------------------------------

    [Fact]
    public async Task ServiceBuilder_BuildsModelService()
    {
      var kernel = await MockKernelAsync();
      await using (kernel)
      {
        var service = new Builder().WithinDriver("docker", kernel)
            .UseModel("ai/smollm2")
            .WithContextSize(4096)
            .KeepRunning(false)
            .Build();

        await using ((System.IAsyncDisposable)service)
        {
          Assert.Equal("ai/smollm2:latest", service.Model.ToString());
          Assert.Equal(ServiceRunningState.Unknown, service.State);
        }
      }
    }

    // ---- B3: extensions + top-level shortcuts --------------------------------

    [Fact]
    public async Task TryUseModelRunner_True_WhenModelPortsPresent()
    {
      var kernel = await MockKernelAsync(enableModels: true);
      await using (kernel)
      {
        var scoped = (IDriverScopedBuilder)new Builder().WithinDriver("docker", kernel).UseModelRunner();
        Assert.True(scoped.TryUseModelRunner(out var rb));
        Assert.NotNull(rb);
      }
    }

    [Fact]
    public async Task TryUseModelRunner_False_WhenNoModelPorts()
    {
      var kernel = await MockKernelAsync(enableModels: false);
      await using (kernel)
      {
        var scoped = (IDriverScopedBuilder)new Builder().WithinDriver("docker", kernel).UseModelRunner();
        Assert.False(scoped.TryUseModelRunner(out var rb));
        Assert.Null(rb);
      }
    }

    // ---- B4: DockerCliDriverPack registration + capabilities -----------------

    [Fact]
    public async Task DockerCliPack_Capabilities_SupportModels()
    {
      var caps = await new DockerCliDriverPack().GetCapabilitiesAsync(TestContext.Current.CancellationToken);
      Assert.True(caps.SupportsModels);
      Assert.True(caps.SupportsModelInference);
    }

    [Fact]
    public async Task DockerCliPack_ResolvesAllThreeModelPorts()
    {
      await using var pack = new DockerCliDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.NotNull(pack.SysCtl<IModelManagementDriver>("docker"));
      Assert.NotNull(pack.SysCtl<IModelRuntimeDriver>("docker"));
      Assert.NotNull(pack.SysCtl<IModelInferenceDriver>("docker"));
    }

    [Fact]
    public async Task DockerCliPack_InferenceHonorsContractOverHttp()
    {
      await using var pack = new DockerCliDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      // Transport is an adapter detail: the CLI pack satisfies the inference
      // contract via the OpenAI-compatible :12434 HTTP data plane (the docker
      // model CLI cannot stream tokens or embed), with no user-facing knob.
      var inference = pack.SysCtl<IModelInferenceDriver>("docker");
      Assert.IsType<DockerApiModelInferenceDriver>(inference);
    }

    // ---- B5: Podman has no model support -------------------------------------

    [Fact]
    public async Task PodmanCliPack_Capabilities_NoModels()
    {
      var caps = await new PodmanCliDriverPack().GetCapabilitiesAsync(TestContext.Current.CancellationToken);
      Assert.False(caps.SupportsModels);
      Assert.False(caps.SupportsModelInference);
    }
  }
}
