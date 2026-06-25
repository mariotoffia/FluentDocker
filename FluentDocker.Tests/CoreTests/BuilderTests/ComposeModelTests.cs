using System;
using System.Linq;
using FluentDocker.Builders.Compose;
using FluentDocker.Services;
using FluentDocker.Tests.CoreTests.Service;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for Compose <c>models:</c> emission (K1), parsing (K2) and the
  /// env-variable binding back to <see cref="ModelRunnerEnvironment"/> (K3).
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

    // ---- K2: parse (round-trip) ----

    [Fact]
    public void Parse_RoundTripsModelsAndBindings()
    {
      var yaml = Sample().EmitOverlay();
      var parsed = ComposeModelBuilder.Parse(yaml);

      var model = Assert.Single(parsed.Models);
      Assert.Equal("llm", model.Key);
      Assert.Equal("ai/smollm2", model.Model);
      Assert.Equal(4096, model.ContextSize);
      Assert.Equal(new[] { "--temp", "0.7" }, model.RuntimeFlags);

      var shortBinding = parsed.Bindings.Single(b => b.Service == "app");
      Assert.Equal("llm", shortBinding.ModelKey);
      Assert.Null(shortBinding.EndpointVar);

      var longBinding = parsed.Bindings.Single(b => b.Service == "api");
      Assert.Equal("llm", longBinding.ModelKey);
      Assert.Equal("AI_MODEL_URL", longBinding.EndpointVar);
      Assert.Equal("AI_MODEL_NAME", longBinding.ModelVar);
    }

    // ---- NEW7: parse flushes pending long-form entries ----

    [Fact]
    public void Parse_LongFormBinding_FollowedBySiblingServiceKey()
    {
      // A long-form binding immediately followed by a sibling service-level key
      // (e.g. image:) must still flush the pending long-form entry.
      const string compose =
          "services:\n" +
          "  api:\n" +
          "    models:\n" +
          "      llm:\n" +
          "        endpoint_var: AI_URL\n" +
          "        model_var: AI_MODEL\n" +
          "    image: my-app:latest\n" +
          "models:\n" +
          "  llm:\n" +
          "    model: ai/qwen3\n";

      var parsed = ComposeModelBuilder.Parse(compose);

      var binding = Assert.Single(parsed.Bindings);
      Assert.Equal("api", binding.Service);
      Assert.Equal("llm", binding.ModelKey);
      Assert.Equal("AI_URL", binding.EndpointVar);
      Assert.Equal("AI_MODEL", binding.ModelVar);
    }

    [Fact]
    public void Parse_MixedShortAndLongFormBindings_OnSameService()
    {
      // A short-form item interleaved after a pending long-form entry must flush the
      // long-form first so neither binding is lost, reordered or duplicated.
      const string compose =
          "services:\n" +
          "  app:\n" +
          "    models:\n" +
          "      first:\n" +
          "        endpoint_var: FIRST_URL\n" +
          "      - second\n" +
          "      third:\n" +
          "        model_var: THIRD_MODEL\n" +
          "models:\n" +
          "  first:\n" +
          "    model: ai/a\n";

      var parsed = ComposeModelBuilder.Parse(compose);

      Assert.Equal(3, parsed.Bindings.Count);

      // Order must be preserved: the pending long-form 'first' has to flush BEFORE the
      // short-form 'second' is emitted, otherwise 'second' jumps ahead of 'first'.
      Assert.Equal(new[] { "first", "second", "third" },
          parsed.Bindings.Select(b => b.ModelKey).ToArray());

      var first = parsed.Bindings.Single(b => b.ModelKey == "first");
      Assert.Equal("app", first.Service);
      Assert.Equal("FIRST_URL", first.EndpointVar);
      Assert.Null(first.ModelVar);

      var second = parsed.Bindings.Single(b => b.ModelKey == "second");
      Assert.Equal("app", second.Service);
      Assert.Null(second.EndpointVar);
      Assert.Null(second.ModelVar);

      var third = parsed.Bindings.Single(b => b.ModelKey == "third");
      Assert.Equal("app", third.Service);
      Assert.Null(third.EndpointVar);
      Assert.Equal("THIRD_MODEL", third.ModelVar);
    }

    [Fact]
    public void Parse_TrailingLongFormBinding_AtEndOfInput()
    {
      // A long-form binding that is the very last thing in the document must be flushed
      // by the end-of-input flush (this already worked) AND not be dropped by an
      // intervening exit branch.
      const string compose =
          "services:\n" +
          "  api:\n" +
          "    models:\n" +
          "      llm:\n" +
          "        endpoint_var: AI_URL\n" +
          "        model_var: AI_MODEL\n";

      var parsed = ComposeModelBuilder.Parse(compose);

      var binding = Assert.Single(parsed.Bindings);
      Assert.Equal("api", binding.Service);
      Assert.Equal("llm", binding.ModelKey);
      Assert.Equal("AI_URL", binding.EndpointVar);
      Assert.Equal("AI_MODEL", binding.ModelVar);
    }

    [Fact]
    public void Parse_RealComposeSnippet()
    {
      const string compose =
          "services:\n" +
          "  app:\n" +
          "    image: my-app:latest\n" +
          "    models:\n" +
          "      - llm\n" +
          "models:\n" +
          "  llm:\n" +
          "    model: ai/qwen3\n" +
          "    context_size: 8192\n";

      var parsed = ComposeModelBuilder.Parse(compose);

      Assert.Equal("ai/qwen3", parsed.Models.Single().Model);
      Assert.Equal(8192, parsed.Models.Single().ContextSize);
      Assert.Contains(parsed.Bindings, b => b.Service == "app" && b.ModelKey == "llm" && b.EndpointVar == null);
    }

    // ---- K3: env binding ----

    [Fact]
    public void Binding_BindsBackViaModelRunnerEnvironment()
    {
      var binding = Sample().Bindings.Single(b => b.Service == "api");
      var previousUrl = Environment.GetEnvironmentVariable(binding.EndpointVar);
      var previousModel = Environment.GetEnvironmentVariable(binding.ModelVar);
      try
      {
        Environment.SetEnvironmentVariable(binding.EndpointVar, "http://model-runner.docker.internal:12434/engines/v1");
        Environment.SetEnvironmentVariable(binding.ModelVar, "ai/smollm2");

        Assert.True(ModelRunnerEnvironment.TryFromVariables(binding.EndpointVar, binding.ModelVar, out var runner));
        Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
        Assert.Equal(new Uri("http://model-runner.docker.internal:12434"), runner.Endpoint);
      }
      finally
      {
        Environment.SetEnvironmentVariable(binding.EndpointVar, previousUrl);
        Environment.SetEnvironmentVariable(binding.ModelVar, previousModel);
      }
    }
  }
}
