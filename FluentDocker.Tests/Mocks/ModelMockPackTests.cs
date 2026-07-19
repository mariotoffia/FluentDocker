using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using Xunit;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Mock-infrastructure self-tests (TESTS-7) for the model-port extensions of
  /// <see cref="MockDriverPack"/> (M1): they assert the mock wiring itself — that the kernel
  /// resolves all three model ports and the Setup* helpers echo their configured values. These
  /// are deliberately mock-echo checks and are NOT product coverage; real service/driver tests
  /// must assert transformed behavior rather than the configured value verbatim.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelMockPackTests
  {
    private static DriverContext Ctx => new("docker");

    [Fact]
    public async Task EnableModelDrivers_ResolvesAllThreePorts()
    {
      var pack = new MockDriverPack()
          .SetupModelList(new ModelInfo { Id = "sha256:1", Reference = ModelReference.Parse("ai/smollm2") })
          .SetupModelStatus(running: true, backend: "llama.cpp")
          .SetupModelChat("Hi there!")
          .EnableModelDrivers();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var mgmt = kernel.SysCtl<IModelManagementDriver>("docker");
        var runtime = kernel.SysCtl<IModelRuntimeDriver>("docker");
        var inference = kernel.SysCtl<IModelInferenceDriver>("docker");

        Assert.NotNull(mgmt);
        Assert.NotNull(runtime);
        Assert.NotNull(inference);

        var list = await mgmt.ListAsync(Ctx, TestContext.Current.CancellationToken);
        Assert.True(list.Success);
        Assert.Single(list.Data);

        var status = await runtime.StatusAsync(Ctx, TestContext.Current.CancellationToken);
        Assert.True(status.Success);
        Assert.True(status.Data.Running);

        var chat = await inference.ChatCompletionAsync(
            Ctx, new ChatCompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken);
        Assert.True(chat.Success);
        Assert.Equal("Hi there!", chat.Data.Choices[0].Message.Content);
      }
    }

    [Fact]
    public async Task SetupModelChatStream_YieldsChunks()
    {
      var pack = new MockDriverPack().SetupModelChatStream("Hel", "lo").EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var inference = kernel.SysCtl<IModelInferenceDriver>("docker");

        var collected = new List<string>();
        await foreach (var chunk in inference.ChatCompletionStreamAsync(
            Ctx, new ChatCompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken))
        {
          collected.Add(chunk.Choices[0].Delta.Content);
        }

        Assert.Equal(new[] { "Hel", "lo" }, collected);
      }
    }

    [Fact]
    public async Task SetupModelEmbeddings_ReturnsVector()
    {
      var pack = new MockDriverPack().SetupModelEmbeddings(0.1f, 0.2f, 0.3f).EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var inference = kernel.SysCtl<IModelInferenceDriver>("docker");
        var resp = await inference.EmbeddingsAsync(
            Ctx, new EmbeddingsRequest { Model = "ai/embeddinggemma" }, TestContext.Current.CancellationToken);

        Assert.True(resp.Success);
        Assert.Equal(3, resp.Data.Data[0].Embedding.Count);
      }
    }

    [Fact]
    public async Task WithoutEnable_ModelPort_NotResolvable()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      await using (kernel)
      {
        Assert.ThrowsAny<System.Exception>(() => kernel.SysCtl<IModelManagementDriver>("docker"));
      }
    }
  }
}
