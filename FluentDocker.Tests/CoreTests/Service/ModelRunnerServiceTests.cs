using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ModelRunnerService"/>: port resolution via the
  /// kernel, <c>CommandResponse</c>→<c>ModelRunnerException</c> translation,
  /// capability computation and the ergonomic chat/embed helpers.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ModelRunnerServiceTests
  {
    private static async Task<(FluentDocker.Kernel.FluentDockerKernel kernel, ModelRunnerService runner)> BuildAsync(
        Action<MockDriverPack> configure = null!, bool enable = true)
    {
      var pack = new MockDriverPack();
      configure?.Invoke(pack);
      if (enable)
        pack.EnableModelDrivers();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"));
      return (kernel, runner);
    }

    [Fact]
    public async Task Store_ListAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelList(
          new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") }));
      await using (kernel)
      {
        var list = await runner.ListAsync(TestContext.Current.CancellationToken);
        Assert.Single(list);
        Assert.Equal("smollm2", list[0].Reference.Name);
      }
    }

    [Fact]
    public async Task Store_ListAsync_UsesRegisteredDriverContext()
    {
      var pack = new MockDriverPack()
          .SetupModelList(new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") })
          .EnableModelDrivers();
      var context = new DriverContext("docker") { Host = "tcp://example:2376" };
      var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new FluentDocker.Kernel.DriverRegistry(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance),
          Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("docker", pack, context, TestContext.Current.CancellationToken);

      await using (kernel)
      {
        var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"));
        await runner.ListAsync(TestContext.Current.CancellationToken);

        pack.ModelManagementDriver.Verify(d => d.ListAsync(
            It.Is<DriverContext>(ctx => ctx.Host == "tcp://example:2376"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task Engine_StatusAsync_UsesRegisteredDriverContextTlsSettings()
    {
      var pack = new MockDriverPack()
          .SetupModelStatus(running: true)
          .EnableModelDrivers();
      var context = new DriverContext("docker")
      {
        CertificatePath = "/certs/docker",
        VerifyTls = false
      };
      var kernel = new FluentDocker.Kernel.FluentDockerKernel(
          new FluentDocker.Kernel.DriverRegistry(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance),
          Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("docker", pack, context, TestContext.Current.CancellationToken);

      await using (kernel)
      {
        var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"));
        await runner.StatusAsync(TestContext.Current.CancellationToken);

        pack.ModelRuntimeDriver.Verify(d => d.StatusAsync(
            It.Is<DriverContext>(ctx => ctx.CertificatePath == "/certs/docker" && ctx.VerifyTls == false),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    public async Task Store_ListAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.ListAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<IList<ModelInfo>>.Fail("boom", ErrorCodes.Model.ListFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.Model.ListFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Store_RemoveAsync_DelegatesToDriver()
    {
      MockDriverPack? capturedPack = null;
      var (kernel, runner) = await BuildAsync(p =>
      {
        p.SetupModelRemove();
        capturedPack = p;
      });
      await using (kernel)
      {
        await runner.RemoveAsync(ModelReference.Parse("ai/smollm2"), force: true, TestContext.Current.CancellationToken);

        // The service must DELEGATE to the management driver with the parsed reference and
        // the requested force flag — assert the actual interaction, not merely "no throw".
        Assert.NotNull(capturedPack);
        capturedPack.ModelManagementDriver.Verify(
            d => d.RemoveAsync(
                It.IsAny<DriverContext>(),
                It.Is<ModelReference>(r => r.Name == "smollm2"),
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
      }
    }

    [Fact]
    public async Task Engine_StatusAsync_Translates()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelStatus(running: true));
      await using (kernel)
      {
        var status = await runner.StatusAsync(TestContext.Current.CancellationToken);
        Assert.True(status.Running);
      }
    }

    [Fact]
    public async Task Engine_LoadAsync_DelegatesToDriver()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelLoad());
      await using (kernel)
      {
        await runner.LoadAsync(ModelReference.Parse("ai/smollm2"), null!, TestContext.Current.CancellationToken);
      }
    }

    [Fact]
    public async Task Inference_ChatCompletionAsync_Translates()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChat("Hello!"));
      await using (kernel)
      {
        var resp = await runner.ChatCompletionAsync(new ChatCompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken);
        Assert.Equal("Hello!", resp.Choices[0].Message.Content);
      }
    }

    [Fact]
    public async Task Inference_ChatAsync_Ergonomic_UsesDefaultModel()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChat("Hi there!"));
      await using (kernel)
      {
        var reply = await runner.ChatAsync("Hello", TestContext.Current.CancellationToken);
        Assert.Equal("Hi there!", reply);
      }
    }

    [Fact]
    public async Task Inference_ChatStreamAsync_ProjectsDeltas()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChatStream("Hel", "lo"));
      await using (kernel)
      {
        var collected = new List<string>();
        await foreach (var token in runner.ChatStreamAsync("hi", TestContext.Current.CancellationToken))
          collected.Add(token);

        Assert.Equal("Hello", string.Concat(collected));
      }
    }

    [Fact]
    public async Task Inference_EmbedAsync_ReturnsFirstVector()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelEmbeddings(0.1f, 0.2f, 0.3f));
      await using (kernel)
      {
        var vector = await runner.EmbedAsync("hello", null!, TestContext.Current.CancellationToken);
        Assert.Equal(3, vector.Count);
        Assert.Equal(0.1f, vector[0], 3);
      }
    }

    [Fact]
    public async Task ChatAsync_EmptyChoices_ThrowsNotSilentlyNull()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChatNoChoices());
      await using (kernel)
        await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ChatAsync("hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Inference_ChatCompletion_Failure_ThrowsWithErrorCode()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChatFailure("nope", ErrorCodes.ModelInference.ModelNotLoaded));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ChatAsync("hi", TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Capabilities_ReflectResolvablePorts()
    {
      var (kernel, runner) = await BuildAsync();
      await using (kernel)
      {
        Assert.True(runner.Capabilities.SupportsManagement);
        Assert.True(runner.Capabilities.SupportsRuntimeControl);
        Assert.True(runner.Capabilities.SupportsInference);
        // Inference is always the full OpenAI-compatible (HTTP) data plane now,
        // so a resolvable inference port implies streaming + embeddings.
        Assert.True(runner.Capabilities.SupportsStreaming);
        Assert.True(runner.Capabilities.SupportsEmbeddings);
      }
    }

    [Fact]
    public async Task Capabilities_WhenNoModelPorts_AllFalse()
    {
      var (kernel, runner) = await BuildAsync(enable: false);
      await using (kernel)
      {
        Assert.False(runner.Capabilities.SupportsManagement);
        Assert.False(runner.Capabilities.SupportsInference);
      }
    }

    [Fact]
    public async Task Properties_ExposeDefaultModelAndEndpoint()
    {
      var (kernel, runner) = await BuildAsync();
      await using (kernel)
      {
        Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
        Assert.Equal(new Uri("http://localhost:12434"), runner.Endpoint);
      }
    }
  }
}
