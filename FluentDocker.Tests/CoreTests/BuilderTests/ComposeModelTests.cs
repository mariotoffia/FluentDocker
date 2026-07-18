using System;
using FluentDocker.Builders.Compose;
using FluentDocker.Services;
using FluentDocker.Tests.CoreTests.Service;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for Compose <c>models:</c> emission (K1) and the env-variable
  /// binding back to <see cref="ModelRunnerEnvironment"/> (K3).
  /// The emitter produces a Compose overlay file (top-level <c>models:</c> + per-service
  /// <c>models:</c> bindings) that merges with a user's compose file via <c>-f</c>.
  /// <para>
  /// The K3 binding test mutates the configured endpoint/model environment variables; this
  /// class joins the non-parallel <see cref="ModelEnvVarsCollection"/> so it never races
  /// other env-mutating model tests on shared process state.
  /// </para>
  /// </summary>
  [Trait("Category", "Unit")]
  [Collection(ModelEnvVarsCollection.Name)]
  public class ComposeModelTests
  {
    private static ComposeModelBuilder Sample()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2").WithContextSize(4096).WithRuntimeFlags("--temp", "0.7"));
      b.BindToService("app", "llm");
      b.BindToService("api", "llm", endpointVar: "AI_MODEL_URL", modelVar: "AI_MODEL_NAME");
      return b;
    }

    // ---- K1: emit ----

    [Fact]
    public void EmitOverlay_TopLevelModels()
    {
      var yaml = Sample().EmitOverlay();

      Assert.Contains("models:", yaml);
      Assert.Contains("  llm:", yaml);
      Assert.Contains("    model: ai/smollm2", yaml);
      Assert.Contains("    context_size: 4096", yaml);
      Assert.Contains("    runtime_flags:", yaml);
      Assert.Contains("\"--temp\"", yaml);
      Assert.Contains("\"0.7\"", yaml);
    }

    [Fact]
    public void EmitOverlay_ShortServiceBinding()
    {
      var yaml = Sample().EmitOverlay();

      // services: app: models: - llm
      Assert.Contains("services:", yaml);
      Assert.Contains("  app:", yaml);
      Assert.Contains("      - llm", yaml);
    }

    [Fact]
    public void EmitOverlay_LongServiceBinding()
    {
      var yaml = Sample().EmitOverlay();

      Assert.Contains("  api:", yaml);
      Assert.Contains("        endpoint_var: AI_MODEL_URL", yaml);
      Assert.Contains("        model_var: AI_MODEL_NAME", yaml);
    }

    // ---- BF-8: fluent-call validation ----

    [Fact]
    public void AddModel_DuplicateKey_ThrowsAtTheFluentCall()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2"));

      var ex = Assert.Throws<ArgumentException>(() =>
          b.AddModel("llm", m => m.WithModel("ai/other")));

      Assert.Contains("llm", ex.Message);
      Assert.Equal("key", ex.ParamName);
    }

    [Fact]
    public void BindToService_SameModelKeyTwiceForOneService_ThrowsAtTheFluentCall()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2"));
      b.BindToService("app", "llm");

      // Mixing short and long form for the same key would emit the key twice in one models: map.
      var ex = Assert.Throws<ArgumentException>(() =>
          b.BindToService("app", "llm", endpointVar: "AI_URL"));

      Assert.Contains("llm", ex.Message);
      Assert.Contains("app", ex.Message);
    }

    [Fact]
    public void BindToService_SameModelKeyOnDifferentServices_IsAllowed()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2"));
      b.BindToService("app", "llm");
      b.BindToService("api", "llm");

      var yaml = b.EmitOverlay();

      Assert.Contains("  app:", yaml);
      Assert.Contains("  api:", yaml);
    }

    [Fact]
    public void AddModel_WithoutWithModel_ThrowsAtTheFluentCall()
    {
      var b = new ComposeModelBuilder();

      var ex = Assert.Throws<ArgumentException>(() =>
          b.AddModel("llm", m => m.WithContextSize(2048)));

      Assert.Contains("WithModel", ex.Message);
    }

    // ---- K3: env binding ----

    [Fact]
    public void Binding_BindsBackViaModelRunnerEnvironment()
    {
      // Sample()'s "api" binding uses these env-var names; emission is verified in K1.
      const string endpointVar = "AI_MODEL_URL";
      const string modelVar = "AI_MODEL_NAME";
      var previousUrl = Environment.GetEnvironmentVariable(endpointVar);
      var previousModel = Environment.GetEnvironmentVariable(modelVar);
      try
      {
        Environment.SetEnvironmentVariable(endpointVar, "http://model-runner.docker.internal:12434/engines/v1");
        Environment.SetEnvironmentVariable(modelVar, "ai/smollm2");

        Assert.True(ModelRunnerEnvironment.TryFromVariables(endpointVar, modelVar, out var runner));
        Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
        Assert.Equal(new Uri("http://model-runner.docker.internal:12434"), runner.Endpoint);
      }
      finally
      {
        Environment.SetEnvironmentVariable(endpointVar, previousUrl);
        Environment.SetEnvironmentVariable(modelVar, previousModel);
      }
    }
  }
}
