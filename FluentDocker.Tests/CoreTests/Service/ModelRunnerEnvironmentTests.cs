using System;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ModelRunnerEnvironment"/> (env parsing → runner)
  /// and <see cref="GenericOpenAiModelRunner"/> (inference-only runner).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelRunnerEnvironmentTests
  {
    private static void WithEnv(string key, string value, Action body)
    {
      var previous = Environment.GetEnvironmentVariable(key);
      try
      {
        Environment.SetEnvironmentVariable(key, value);
        body();
      }
      finally
      {
        Environment.SetEnvironmentVariable(key, previous);
      }
    }

    [Fact]
    public void FromEnvironment_DefaultPrefix_ReadsLlmVars()
    {
      WithEnv("LLM_URL", "http://10.0.0.5:12434", () =>
        WithEnv("LLM_MODEL", "ai/smollm2", () =>
        {
          var runner = ModelRunnerEnvironment.FromEnvironment();
          Assert.Equal(new Uri("http://10.0.0.5:12434"), runner.Endpoint);
          Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
          Assert.True(runner.Capabilities.SupportsInference);
          Assert.False(runner.Capabilities.SupportsManagement);
        }));
    }

    [Fact]
    public void FromEnvironment_CustomPrefix_ReadsPrefixedVars()
    {
      WithEnv("AI_MODEL_URL", "http://host:9000", () =>
        WithEnv("AI_MODEL_MODEL", "ai/qwen3", () =>
        {
          var runner = ModelRunnerEnvironment.FromEnvironment("AI_MODEL");
          Assert.Equal(new Uri("http://host:9000"), runner.Endpoint);
          Assert.Equal("ai/qwen3:latest", runner.DefaultModel.ToString());
        }));
    }

    [Fact]
    public void FromEnvironment_MissingUrl_Throws()
    {
      WithEnv("LLM_URL", null, () =>
        WithEnv("LLM_MODEL", "ai/x", () =>
            Assert.Throws<InvalidOperationException>(() => ModelRunnerEnvironment.FromEnvironment())));
    }

    [Fact]
    public void TryFromEnvironment_Unset_ReturnsFalse()
    {
      WithEnv("LLM_URL", null, () =>
      {
        Assert.False(ModelRunnerEnvironment.TryFromEnvironment(out var runner));
        Assert.Null(runner);
      });
    }

    [Fact]
    public async Task GenericRunner_ChatAsync_UsesInjectedConnection()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), new DockerApiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      var reply = await runner.ChatAsync("hi", TestContext.Current.CancellationToken);
      Assert.False(string.IsNullOrEmpty(reply));
    }

    [Fact]
    public async Task GenericRunner_Management_NotSupported()
    {
      var conn = new MockModelApiConnection();
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/x"), new DockerApiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      await Assert.ThrowsAsync<NotSupportedException>(() => runner.ListAsync(TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<NotSupportedException>(() => runner.LoadAsync(ModelReference.Parse("ai/x")));
    }

    [Fact]
    public async Task GenericRunner_EmbedAsync_Works()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, DmrFixtures.Load("embeddings.json"));
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/embeddinggemma"), new DockerApiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      var vector = await runner.EmbedAsync("hi", null, TestContext.Current.CancellationToken);
      Assert.NotEmpty(vector);
    }
  }
}
