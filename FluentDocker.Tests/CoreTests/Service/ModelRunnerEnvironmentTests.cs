using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
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
  /// <para>
  /// Several tests mutate process-wide environment variables (<c>LLM_URL</c>,
  /// <c>LLM_MODEL</c>, <c>AI_MODEL_*</c>). They are placed in the non-parallel
  /// <see cref="ModelEnvVarsCollection"/> so they never run concurrently with other
  /// env-mutating model tests (which would race on the same global state); each test still
  /// restores the variable(s) it set via <see cref="WithEnv"/>'s try/finally.
  /// </para>
  /// </summary>
  [Trait("Category", "Unit")]
  [Collection(ModelEnvVarsCollection.Name)]
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
          // DefaultModel is still the Docker artifact reference (keeps :latest)...
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
      WithEnv("LLM_URL", null!, () =>
        WithEnv("LLM_MODEL", "ai/x", () =>
            Assert.Throws<InvalidOperationException>(() => ModelRunnerEnvironment.FromEnvironment())));
    }

    [Fact]
    public void TryFromEnvironment_Unset_ReturnsFalse()
    {
      WithEnv("LLM_URL", null!, () =>
      {
        Assert.False(ModelRunnerEnvironment.TryFromEnvironment(out var runner));
        Assert.Null(runner);
      });
    }

    [Fact]
    public async Task GenericRunner_ChatAsync_UsesInjectedConnection()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      var reply = await runner.ChatAsync("hi", TestContext.Current.CancellationToken);
      Assert.False(string.IsNullOrEmpty(reply));
    }

    [Fact]
    public async Task GenericRunner_Management_NotSupported()
    {
      var conn = new MockModelApiConnection();
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/x"), new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      await Assert.ThrowsAsync<NotSupportedException>(() => runner.ListAsync(TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<NotSupportedException>(() => runner.LoadAsync(ModelReference.Parse("ai/x"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenericRunner_EmbedAsync_Works()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, DmrFixtures.Load("embeddings.json"));
      await using var runner = new GenericOpenAiModelRunner(ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/embeddinggemma"), new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      var vector = await runner.EmbedAsync("hi", null!, TestContext.Current.CancellationToken);
      Assert.NotEmpty(vector);
    }

    // ======================== H1: verbatim inference id ===================

    [Fact]
    public async Task GenericRunner_RemoteModelId_SentVerbatim_NotLatestTagged()
    {
      // Mirrors how ModelRunnerEnvironment composes a remote runner: the model id is a
      // raw/remote OpenAI id preserved verbatim (defaultInferenceId), so a real
      // OpenAI-compatible endpoint accepts it. Regression for H1: "gpt-4o-mini" must
      // NOT become "gpt-4o-mini:latest" in the request body.
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      await using var runner = new GenericOpenAiModelRunner(
          ModelRunnerEndpoint.HostTcp(), defaultModel: null!,
          new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()),
          conn.PingAsync, conn, defaultInferenceId: new InferenceModelId("gpt-4o-mini"));

      await runner.ChatAsync("hi", TestContext.Current.CancellationToken);

      var body = conn.GetRequests().Single(r => r.Path.EndsWith(
          "/chat/completions", StringComparison.Ordinal)).Body;
      Assert.Contains("\"model\":\"gpt-4o-mini\"", body);
      Assert.DoesNotContain("gpt-4o-mini:latest", body);
    }

    [Fact]
    public async Task GenericRunner_DockerRefBareName_InfersWithoutLatest()
    {
      // A bare Docker ref ("ai/smollm2") keeps :latest as a ModelReference, but the
      // inference body must carry the bare id (no :latest).
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      var reference = ModelReference.Parse("ai/smollm2");
      await using var runner = new GenericOpenAiModelRunner(
          ModelRunnerEndpoint.HostTcp(), reference,
          new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      await runner.ChatAsync("hi", TestContext.Current.CancellationToken);

      // Management form still serializes with :latest...
      Assert.Equal("ai/smollm2:latest", reference.ToString());
      Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());

      // ...but the inference body uses the bare id.
      var body = conn.GetRequests().Single(r => r.Path.EndsWith(
          "/chat/completions", StringComparison.Ordinal)).Body;
      Assert.Contains("\"model\":\"ai/smollm2\"", body);
      Assert.DoesNotContain("ai/smollm2:latest", body);
    }

    // ======================== D15: IInferenceModelRunner narrow interface ======

    [Fact]
    public async Task GenericRunner_ImplementsIInferenceModelRunner()
    {
      // D15: GenericOpenAiModelRunner must satisfy IInferenceModelRunner so callers
      // that only need the inference plane can use the narrow interface.
      var conn = new MockModelApiConnection();
      await using var runner = new GenericOpenAiModelRunner(
          ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/x"),
          new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp()), conn.PingAsync, conn);

      Assert.IsAssignableFrom<FluentDocker.Services.IInferenceModelRunner>(runner);
      Assert.IsAssignableFrom<FluentDocker.Services.IModelRunner>(runner); // back-compat
    }

    [Fact]
    public void ModelRunnerEnvironment_CreateInferenceRunner_ReturnsNarrowType()
    {
      // D16: ModelRunnerEnvironment.CreateInferenceRunner composes the runner inline
      // and returns IInferenceModelRunner (the narrow type), not the concrete class.
      var endpoint = ModelRunnerEndpoint.HostTcp();
      var runner = FluentDocker.Services.ModelRunnerEnvironment.CreateInferenceRunner(endpoint, "ai/smollm2");

      Assert.IsAssignableFrom<FluentDocker.Services.IInferenceModelRunner>(runner);
      // IInferenceModelRunner also satisfies IModelRunner because GenericOpenAiModelRunner
      // implements both; we verify the narrow type is what is returned at the API surface.
    }

    [Fact]
    public void ModelRunnerEnvironment_TryFromEnvironment_ReturnedRunnerIsIInferenceModelRunner()
    {
      // TryFromEnvironment result is castable to IInferenceModelRunner (D15 integration).
      WithEnv("LLM_URL", "http://10.0.0.1:12434", () =>
        WithEnv("LLM_MODEL", "ai/smollm2", () =>
        {
          Assert.True(FluentDocker.Services.ModelRunnerEnvironment.TryFromEnvironment(out var runner));
          Assert.IsAssignableFrom<FluentDocker.Services.IInferenceModelRunner>(runner);
        }));
    }

    // ======================== MR9: env URL validation parity ==================

    [Theory]
    [InlineData("ftp://10.0.0.5:12434")]  // valid absolute URI (pre-fix accepted it) but wrong scheme
    [InlineData("file:///etc/passwd")]    // absolute, but no host
    [InlineData("localhost:12434")]       // scheme-less: parses absolute with scheme 'localhost', no host
    public void TryFromEnvironment_NonHttpOrSchemeless_ReturnsFalse(string url)
    {
      // MR9: TryFromEnvironment must apply the SAME http(s)+host validation as
      // ModelRunnerEndpoint.TryFromEnvironment, so an invalid scheme fails the Try* API here
      // instead of being accepted and blowing up later.
      WithEnv("LLM_URL", url, () =>
      {
        Assert.False(ModelRunnerEnvironment.TryFromEnvironment(out var runner));
        Assert.Null(runner);
      });
    }

    [Theory]
    [InlineData("ftp://10.0.0.5:12434")]
    [InlineData("file:///etc/passwd")]
    [InlineData("localhost:12434")]
    public void TryFromVariables_NonHttpOrSchemeless_ReturnsFalse(string url)
    {
      // MR9: the explicitly-named-variable path must reject the same invalid values.
      WithEnv("AI_MODEL_URL", url, () =>
      {
        Assert.False(ModelRunnerEnvironment.TryFromVariables("AI_MODEL_URL", "AI_MODEL_MODEL", out var runner));
        Assert.Null(runner);
      });
    }

    [Theory]
    [InlineData("http://host:12434")]
    [InlineData("https://host:12434")]
    public void TryFromEnvironment_ValidHttp_ReturnsTrue(string url)
    {
      WithEnv("LLM_URL", url, () =>
        WithEnv("LLM_MODEL", "ai/x", () =>
            Assert.True(ModelRunnerEnvironment.TryFromEnvironment(out _))));
    }

    // ======================== MR6: env runner honors TLS/transport config =====

    [Fact]
    public void CreateInferenceRunner_DefaultConfig_RefusesApiKeyOverPlaintextToRemoteHost()
    {
      // Baseline for the seam below: with the default config (VerifyTls=true) the connection ctor
      // refuses to send a bearer token over plaintext HTTP to a NON-loopback host.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://10.0.0.5:12434"));
      Assert.Throws<ModelRunnerException>(() =>
          ModelRunnerEnvironment.CreateInferenceRunner(endpoint, "ai/x", apiKey: "secret"));
    }

    [Fact]
    public async Task CreateInferenceRunner_WithConfig_PropagatesTlsSettingsIntoConnection()
    {
      // MR6: the config overload must actually thread the config into the ModelApiConnection.
      // Behavioral seam: VerifyTls=false is exactly what the connection ctor consults to permit an
      // API key over plaintext HTTP to a remote host — so if (and only if) the config reaches the
      // connection, construction succeeds instead of throwing as the default-config case does above.
      var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://10.0.0.5:12434"));
      var config = new ModelApiConnectionConfig { VerifyTls = false, RequestTimeout = TimeSpan.FromSeconds(5) };

      var runner = ModelRunnerEnvironment.CreateInferenceRunner(endpoint, "ai/x", config, apiKey: "secret");

      await using ((IAsyncDisposable)runner)
      {
        Assert.NotNull(runner);
        Assert.IsAssignableFrom<IInferenceModelRunner>(runner);
      }
    }
  }
}
