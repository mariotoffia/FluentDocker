using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
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

    [Fact]
    public async Task WithInferenceDriver_Explicit_RoutesInferenceToSuppliedDriver()
    {
      // Scoped "docker" pack provides management + its OWN inference ("from-pack").
      var pack = new MockDriverPack().SetupModelChat("from-pack").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        // A separately-constructed inference driver returns "from-injected".
        var injected = new Mock<IModelInferenceDriver>();
        injected.Setup(d => d.ChatCompletionAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<ChatCompletionResponse>.Ok(new ChatCompletionResponse
            {
              Choices = [new ChatChoice { Index = 0, FinishReason = "stop", Message = new ChatMessage { Role = "assistant", Content = "from-injected" } }]
            }));

        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithInferenceDriver(injected.Object)
            .BuildAsync(TestContext.Current.CancellationToken);

        // Inference is served by the injected driver; management still resolves from "docker".
        var reply = await runner.ChatAsync("hi", TestContext.Current.CancellationToken);
        Assert.Equal("from-injected", reply);
      }
    }

    [Fact]
    public async Task WithInferenceDriver_ByDriverId_ResolvesInferenceFromAnotherRegisteredDriver()
    {
      // "docker" = management plane (+ its own inference "from-docker").
      var docker = new MockDriverPack().SetupModelChat("from-docker").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", docker);
      await using (kernel)
      {
        // A SECOND registered driver "remote" whose inference returns "from-remote".
        var remote = new MockDriverPack().SetupModelChat("from-remote").EnableModelDrivers();
        var remoteCtx = new DriverContext("remote");
        await remote.InitializeAsync(remoteCtx);
        await kernel.RegisterDriverPackAsync("remote", remote, remoteCtx);

        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithInferenceDriver("remote")
            .BuildAsync(TestContext.Current.CancellationToken);

        // Inference resolves from "remote", not from the scoped "docker" driver.
        var reply = await runner.ChatAsync("hi", TestContext.Current.CancellationToken);
        Assert.Equal("from-remote", reply);
      }
    }

    [Fact]
    public async Task RunnerBuilder_WithBackend_Auto_DoesNotForceConfigure()
    {
      // "auto" is the default no-op — it must not, on its own, trigger a configure call.
      var pack = new MockDriverPack().SetupModelConfigure().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithBackend("auto")
            .BuildAsync(TestContext.Current.CancellationToken);

        pack.ModelRuntimeDriver.Verify(d => d.ConfigureAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<FluentDocker.Model.Models.Options.ModelConfigureOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
      }
    }

    [Fact]
    public async Task RunnerBuilder_WithBackend_Explicit_FlowsToConfigure()
    {
      var pack = new MockDriverPack().SetupModelConfigure().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await using var runner = await new Builder().WithinDriver("docker", kernel)
            .UseModelRunner()
            .ForModel("ai/smollm2")
            .WithBackend("vllm")
            .BuildAsync(TestContext.Current.CancellationToken);

        pack.ModelRuntimeDriver.Verify(d => d.ConfigureAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.Is<FluentDocker.Model.Models.Options.ModelConfigureOptions>(o => o.Backend == "vllm" && !o.IsAutoBackend),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task RunnerBuilder_PostBuildPullFailure_Propagates_WithoutLeakingOwnedConnection()
    {
      // A custom endpoint makes the builder create + OWN an inference connection. If the
      // build-time pull then fails, the builder must dispose the runner (and its owned
      // connection) and rethrow — not leak it.
      var pack = new MockDriverPack().SetupModelConfigure().EnableModelDrivers();
      pack.ModelManagementDriver
          .Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<System.IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new System.InvalidOperationException("pull boom"));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        await Assert.ThrowsAsync<System.InvalidOperationException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseModelRunner()
                .ForModel("ai/smollm2")
                .WithEndpoint(ModelRunnerEndpoint.ContainerInternal())
                .PullIfMissing()
                .BuildAsync(TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    public async Task ModelRunnerService_DisposeAsync_DisposesOwnedResource()
    {
      // The owned-resource disposal contract the builder relies on for cleanup.
      var kernel = await FluentDocker.Kernel.FluentDockerKernel
          .Create(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).BuildAsync();
      await using (kernel)
      {
        var owned = new DisposeSpy();
        var service = new FluentDocker.Services.Impl.ModelRunnerService(
            kernel, "docker", ModelRunnerEndpoint.HostTcp(), ownedResource: owned);

        await service.DisposeAsync();
        await service.DisposeAsync(); // idempotent

        Assert.Equal(1, owned.DisposeCount);
      }
    }

    private sealed class DisposeSpy : System.IAsyncDisposable
    {
      public int DisposeCount { get; private set; }

      public ValueTask DisposeAsync()
      {
        DisposeCount++;
        return ValueTask.CompletedTask;
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

    [Fact]
    public async Task ServiceBuilder_BuildAsync_PassesCancellationToPull()
    {
      using var cts = new CancellationTokenSource();
      var pack = new MockDriverPack()
          .SetupModelPull(new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") })
          .SetupModelConfigure()
          .EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var service = await new Builder().WithinDriver("docker", kernel)
            .UseModel("ai/smollm2")
            .PullIfMissing()
            .BuildAsync(cts.Token);

        await using ((System.IAsyncDisposable)service)
        {
          Assert.Equal("ai/smollm2:latest", service.Model.ToString());

          // The build-time pull must receive the caller's cancellation token.
          pack.ModelManagementDriver.Verify(d => d.PullAsync(
              It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<System.IProgress<ModelPullProgress>>(),
              It.Is<CancellationToken>(t => t == cts.Token)), Times.Once);
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
