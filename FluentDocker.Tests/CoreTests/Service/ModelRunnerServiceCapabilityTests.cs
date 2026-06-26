using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for backend advertisement (E1) and the clean partial-pack
  /// <see cref="System.NotSupportedException"/> contract (E3) of
  /// <see cref="ModelRunnerService"/> and <see cref="GenericOpenAiModelRunner"/>.
  /// The backend is sourced from a driver implementing <see cref="IModelBackendInfo"/>
  /// — never a hardcoded <c>llama.cpp</c>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelRunnerServiceCapabilityTests
  {
    private static async Task<(FluentDockerKernel kernel, ModelRunnerService runner)> BuildAsync(MockDriverPack pack)
    {
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"));
      return (kernel, runner);
    }

    // ==================== E1: backend comes from the driver ====================

    [Fact]
    public async Task Capabilities_BackendSourcedFromRuntimeDriver()
    {
      // A runtime driver that ALSO implements IModelBackendInfo advertises its engine,
      // so the runner reports that backend rather than a hardcoded one.
      var pack = new MockDriverPack().SetupRuntimeBackend("vllm").EnableModelDrivers();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        Assert.Equal("vllm", runner.Capabilities.DefaultBackend);
        Assert.Contains("vllm", runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task Capabilities_NoBackendInfoDriver_ReportsNoBackend()
    {
      // No resolvable driver implements IModelBackendInfo → no backend is assumed.
      var pack = new MockDriverPack().EnableModelDrivers();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        Assert.Null(runner.Capabilities.DefaultBackend);
        Assert.Empty(runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task Capabilities_InferenceOverride_DoesNotReportScopedRuntimeBackend()
    {
      var pack = new MockDriverPack().SetupRuntimeBackend("llama.cpp").EnableModelDrivers();
      var (kernel, _) = await BuildAsync(pack);
      await using (kernel)
      {
        var inference = new Mock<IModelInferenceDriver>().Object;
        var runner = new ModelRunnerService(
            kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), inference);

        Assert.Null(runner.Capabilities.DefaultBackend);
        Assert.Empty(runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task Capabilities_NoInferenceOverride_ReportsScopedRuntimeBackend()
    {
      var pack = new MockDriverPack().SetupRuntimeBackend("llama.cpp").EnableModelDrivers();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        Assert.Equal("llama.cpp", runner.Capabilities.DefaultBackend);
        Assert.Contains("llama.cpp", runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task Capabilities_NoInferenceOverride_ReportsScopedInferenceBackendBeforeRuntimeBackend()
    {
      var pack = new MockDriverPack().SetupRuntimeBackend("llama.cpp");
      var backend = pack.ModelInferenceDriver.As<IModelBackendInfo>();
      backend.SetupGet(b => b.DefaultBackend).Returns("vllm");
      backend.SetupGet(b => b.AvailableBackends).Returns(new[] { "vllm" });
      pack.EnableModelDrivers();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        Assert.Equal("vllm", runner.Capabilities.DefaultBackend);
        Assert.Contains("vllm", runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task Capabilities_InferenceOverrideWithBackendInfo_ReportsOverrideBackend()
    {
      var pack = new MockDriverPack().SetupRuntimeBackend("llama.cpp").EnableModelDrivers();
      var (kernel, _) = await BuildAsync(pack);
      await using (kernel)
      {
        var inference = new Mock<IModelInferenceDriver>();
        var backend = inference.As<IModelBackendInfo>();
        backend.SetupGet(b => b.DefaultBackend).Returns("vllm");
        backend.SetupGet(b => b.AvailableBackends).Returns(new[] { "vllm" });
        var runner = new ModelRunnerService(
            kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), inference.Object);

        Assert.Equal("vllm", runner.Capabilities.DefaultBackend);
        Assert.Contains("vllm", runner.Capabilities.AvailableBackends);
      }
    }

    [Fact]
    public async Task GenericRunner_Capabilities_ReportsNoBackend()
    {
      // The generic runner serves arbitrary OpenAI-compatible endpoints, so the backend
      // is genuinely unknown — it must NOT claim llama.cpp.
      var inference = new Mock<IModelInferenceDriver>().Object;
      await using var runner = new GenericOpenAiModelRunner(
          ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), inference);

      Assert.Null(runner.Capabilities.DefaultBackend);
      Assert.Empty(runner.Capabilities.AvailableBackends);
    }

    // ============ TEST-4: inference-only custom pack (couples to E3) ============

    [Fact]
    public async Task InferenceOnlyPack_ReportsInferenceOnlyCapabilities()
    {
      var pack = new MockDriverPack().SetupModelChat("hi there").EnableModelInferenceOnly();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        Assert.True(runner.Capabilities.SupportsInference);
        Assert.False(runner.Capabilities.SupportsManagement);
        Assert.False(runner.Capabilities.SupportsRuntimeControl);
      }
    }

    [Fact]
    public async Task InferenceOnlyPack_ChatAsync_ReturnsCannedContent()
    {
      var pack = new MockDriverPack().SetupModelChat("canned-reply").EnableModelInferenceOnly();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        var reply = await runner.ChatAsync("hi", TestContext.Current.CancellationToken);
        Assert.Equal("canned-reply", reply);
      }
    }

    [Fact]
    public async Task InferenceOnlyPack_StoreOp_ThrowsCleanNotSupported()
    {
      var pack = new MockDriverPack().SetupModelChat("hi").EnableModelInferenceOnly();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        // A missing management port surfaces a clean NotSupportedException naming the
        // capability, never the driver-layer InterfaceNotSupportedException.
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => runner.ListAsync(TestContext.Current.CancellationToken));
        Assert.Contains("management", ex.Message, StringComparison.OrdinalIgnoreCase);
      }
    }

    [Fact]
    public async Task InferenceOnlyPack_EngineOp_ThrowsCleanNotSupported()
    {
      var pack = new MockDriverPack().SetupModelChat("hi").EnableModelInferenceOnly();
      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => runner.LoadAsync(ModelReference.Parse("ai/smollm2"), null, TestContext.Current.CancellationToken));
        Assert.Contains("runtime", ex.Message, StringComparison.OrdinalIgnoreCase);
      }
    }

    // ============ TEST-3: concurrent ChatAsync on one runner ============

    [Fact]
    public async Task ChatAsync_ManyConcurrentCalls_AllSucceedWithoutCrosstalk()
    {
      // The inference mock echoes back each request's user prompt, so any shared-state
      // corruption inside the runner would surface as a mismatched/duplicated reply.
      var pack = new MockDriverPack();
      pack.ModelInferenceDriver
          .Setup(d => d.ChatCompletionAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((DriverContext ctx, ChatCompletionRequest req, CancellationToken token) =>
              CommandResponse<ChatCompletionResponse>.Ok(new ChatCompletionResponse
              {
                Choices = new List<ChatChoice>
                {
                  new() { Index = 0, FinishReason = "stop", Message = new ChatMessage { Role = "assistant", Content = req.Messages[req.Messages.Count - 1].Content } }
                }
              }));
      pack.EnableModelDrivers();

      var (kernel, runner) = await BuildAsync(pack);
      await using (kernel)
      {
        var ct = TestContext.Current.CancellationToken;
        const int count = 32;
        var tasks = Enumerable.Range(0, count).Select(i => runner.ChatAsync($"hi-{i}", ct)).ToArray();
        var replies = await Task.WhenAll(tasks);

        for (var i = 0; i < count; i++)
          Assert.Equal($"hi-{i}", replies[i]);
      }
    }
  }
}
