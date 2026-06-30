using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for <see cref="ContainerModelBuilderExtensions.WithModel"/> (B6):
  /// the model endpoint is injected as env + an Engine host-gateway alias, and
  /// <c>localhost</c> is rejected for container consumers.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ContainerModelWiringTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    private static async Task<FluentDocker.Kernel.FluentDockerKernel> KernelAsync()
    {
      var pack = new MockDriverPack().SetupContainerCreate().SetupContainerStart().SetupContainerInspect(running: true);
      return await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
    }

    [Fact]
    public async Task WithModel_InjectsEnv_AndHostGateway()
    {
      var pack = new MockDriverPack().SetupContainerCreate().SetupContainerStart().SetupContainerInspect(running: true);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        new Builder().WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("my-app:latest").WithModel(Model))
            .Build();

        pack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(),
            It.Is<ContainerCreateConfig>(cfg =>
                cfg.Environment.ContainsKey("LLM_URL")
                && cfg.Environment["LLM_URL"].Contains("model-runner.docker.internal")
                && cfg.Environment["LLM_URL"].Contains("/engines/v1")
                && cfg.Environment.ContainsKey("LLM_MODEL")
                && cfg.Environment["LLM_MODEL"] == "ai/smollm2"
                && cfg.ExtraHosts.ContainsKey("model-runner.docker.internal")
                && cfg.ExtraHosts["model-runner.docker.internal"] == "host-gateway"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task WithModel_CustomEnvVarNames()
    {
      var pack = new MockDriverPack().SetupContainerCreate().SetupContainerStart().SetupContainerInspect(running: true);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        new Builder().WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("app").WithModel(Model, endpointVar: "AI_URL", modelVar: "AI_MODEL"))
            .Build();

        pack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(),
            It.Is<ContainerCreateConfig>(cfg => cfg.Environment.ContainsKey("AI_URL") && cfg.Environment.ContainsKey("AI_MODEL")),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task WithModel_BridgeGateway_NoHostGatewayAlias()
    {
      var pack = new MockDriverPack().SetupContainerCreate().SetupContainerStart().SetupContainerInspect(running: true);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        new Builder().WithinDriver("docker", kernel)
            .UseContainer(c => c.UseImage("app").WithModel(Model, ModelRunnerEndpoint.Custom(new Uri("http://172.17.0.1:12434"))))
            .Build();

        pack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(),
            It.Is<ContainerCreateConfig>(cfg =>
                cfg.Environment["LLM_URL"].Contains("172.17.0.1") && cfg.ExtraHosts.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task WithModel_Localhost_Rejected()
    {
      var kernel = await KernelAsync();
      await using (kernel)
      {
        Assert.Throws<ArgumentException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseContainer(c => c.UseImage("app").WithModel(Model, ModelRunnerEndpoint.HostTcp())));
      }
    }

    [Theory]
    [InlineData("http://[::1]:12434")]      // IPv6 loopback
    [InlineData("http://127.0.0.2:12434")]  // any 127.0.0.0/8 address is loopback
    [InlineData("http://[::1]:12434/engines/v1")]
    public async Task WithModel_LoopbackAlias_Rejected(string url)
    {
      // A container cannot reach the runner via ANY loopback alias (not just
      // localhost/127.0.0.1) — they resolve to the container itself.
      var kernel = await KernelAsync();
      await using (kernel)
      {
        Assert.Throws<ArgumentException>(() =>
            new Builder().WithinDriver("docker", kernel)
                .UseContainer(c => c.UseImage("app").WithModel(Model, ModelRunnerEndpoint.Custom(new Uri(url)))));
      }
    }
  }
}
