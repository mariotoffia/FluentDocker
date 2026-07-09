using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.CoreTests.Service;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  [Collection(ModelEnvVarsCollection.Name)]
  public sealed class ModelRunnerProdReadyTests
  {
    [Fact]
    public void ChatMessage_ArrayContent_ConcatenatesTextParts()
    {
      const string json =
          "{\"role\":\"assistant\",\"content\":[" +
          "{\"type\":\"text\",\"text\":\"hi \"}," +
          "{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,abc\"}}," +
          "{\"type\":\"text\",\"text\":\"there\"}]}";

      var message = JsonHelper.TryDeserialize<ChatMessage>(json);

      Assert.NotNull(message);
      Assert.Equal("hi there", message.Content);
    }

    [Fact]
    public void ChatMessage_StringContent_DeserializesUnchanged()
    {
      var message = JsonHelper.TryDeserialize<ChatMessage>(
          "{\"role\":\"assistant\",\"content\":\"plain text\"}");

      Assert.NotNull(message);
      Assert.Equal("plain text", message.Content);
    }

    [Fact]
    public void ChatMessage_StringContent_RoundTripsAsString()
    {
      var json = JsonHelper.Serialize(new ChatMessage { Role = "user", Content = "hello" });
      var message = JsonHelper.TryDeserialize<ChatMessage>(json);

      Assert.Contains("\"content\":\"hello\"", json, StringComparison.Ordinal);
      Assert.NotNull(message);
      Assert.Equal("hello", message.Content);
    }

    [Fact]
    public void Default_SetButInvalidEnvironment_ThrowsInsteadOfLocalhostFallback()
    {
      var previous = Environment.GetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, "remote-host:12434");

        var ex = Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.Default());

        Assert.Contains(ModelRunnerEndpoint.UrlEnvironmentVariable, ex.Message, StringComparison.Ordinal);
        Assert.Contains("remote-host:12434", ex.Message, StringComparison.Ordinal);
      }
      finally
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, previous);
      }
    }

    [Fact]
    public void Default_UnsetEnvironment_ReturnsHostTcp()
    {
      var previous = Environment.GetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable);
      try
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, null);

        Assert.Equal(new Uri("http://localhost:12434"), ModelRunnerEndpoint.Default().BaseAddress);
      }
      finally
      {
        Environment.SetEnvironmentVariable(ModelRunnerEndpoint.UrlEnvironmentVariable, previous);
      }
    }

    [Theory]
    [InlineData("llama.cpp")]
    [InlineData("vllm_2-rc1")]
    [InlineData("abc123")]
    public void Engine_AllowsUrlPathSafeTokens(string engine)
    {
      var endpoint = ModelRunnerEndpoint.HostTcp(engine: engine);

      Assert.Equal(engine, endpoint.Engine);
    }

    [Theory]
    [InlineData("llama.cpp/../admin")]
    [InlineData("a?b")]
    [InlineData("bad engine")]
    public void Engine_RejectsUnsafeTokens(string engine)
    {
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.HostTcp(engine: engine));
    }

    [Fact]
    public void Raw_StripsUserInfoFromBaseAddress()
    {
      var endpoint = ModelRunnerEndpoint.Raw(new Uri("https://u:p@runner.example.com:9443/v1"));

      Assert.Equal(new Uri("https://runner.example.com:9443"), endpoint.BaseAddress);
      Assert.DoesNotContain("@", endpoint.BaseAddress.ToString(), StringComparison.Ordinal);
      Assert.Equal(new Uri("https://runner.example.com:9443/v1/models"), endpoint.ResolveUri("/models"));
    }

    [Fact]
    public async Task EndpointUnreachable_MessageContainsAttemptedBaseAddress()
    {
      await using var connection = new ModelApiConnection(
          new Uri("http://10.0.0.9:12434"),
          new ThrowingHandler(new HttpRequestException("connection refused")));

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(
          () => connection.GetAsync("/models", TestContext.Current.CancellationToken));

      Assert.Contains("http://10.0.0.9:12434", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionRequest_SeedAcceptsLongRange()
    {
      var json = JsonHelper.Serialize(new CompletionRequest
      {
        Model = "ai/smollm2",
        Prompt = "hello",
        Seed = 4_294_967_295L
      });

      Assert.Contains("4294967295", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbedAsync_EmptyEmbeddingData_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildRunnerAsync(pack =>
          pack.ModelInferenceDriver
              .Setup(d => d.EmbeddingsAsync(
                  It.IsAny<DriverContext>(), It.IsAny<EmbeddingsRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<EmbeddingsResponse>.Ok(new EmbeddingsResponse
              {
                Object = "list",
                Model = "mock",
                Data = new List<EmbeddingData>()
              })));

      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.EmbedAsync("hello", null!, TestContext.Current.CancellationToken));

        Assert.Contains("no embedding", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      }
    }

    private static async Task<(FluentDocker.Kernel.FluentDockerKernel kernel, ModelRunnerService runner)> BuildRunnerAsync(
        Action<MockDriverPack> configure)
    {
      var pack = new MockDriverPack();
      configure(pack);
      pack.EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(
          kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"));
      return (kernel, runner);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
      private readonly Exception _exception;

      public ThrowingHandler(Exception exception) => _exception = exception;

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
          Task.FromException<HttpResponseMessage>(_exception);
    }
  }
}
