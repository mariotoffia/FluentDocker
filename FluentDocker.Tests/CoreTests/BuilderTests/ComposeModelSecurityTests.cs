using System;
using FluentDocker.Builders.Compose;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Security tests for <see cref="ComposeModelBuilder.EmitOverlay"/>: the overlay is
  /// written to disk and merged into a real Compose project, so a newline / control
  /// character in any scalar must never be able to inject arbitrary Compose YAML.
  /// Keys, service names and env-var names are validated as identifiers; the free-form
  /// model reference and runtime flags are rejected when they contain line breaks.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ComposeModelSecurityTests
  {
    [Fact]
    public void ModelKeyWithNewline_Throws()
    {
      // BLDR-8: identifier/injection validation now fails at the fluent call, not at emission.
      var b = new ComposeModelBuilder();

      Assert.Throws<ArgumentException>(() =>
          b.AddModel("llm\n    injected:\n      model: evil/x", m => m.WithModel("ai/smollm2")));
    }

    [Fact]
    public void ModelReferenceWithNewline_Throws()
    {
      var b = new ComposeModelBuilder();

      Assert.Throws<ArgumentException>(() =>
          b.AddModel("llm", m => m.WithModel("ai/smollm2\n    injected: true")));
    }

    [Fact]
    public void ServiceNameWithNewline_Throws()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2"));

      Assert.Throws<ArgumentException>(() =>
          b.BindToService("app\n  evilservice:\n    image: attacker/x", "llm"));
    }

    [Fact]
    public void EndpointVarWithNewline_Throws()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2"));

      Assert.Throws<ArgumentException>(() =>
          b.BindToService("app", "llm", endpointVar: "URL\n        model_var: INJECTED"));
    }

    [Fact]
    public void EmitOverlay_RuntimeFlagWithNewline_Throws()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2").WithRuntimeFlags("--temp", "0.7\"\n      - \"--injected"));

      Assert.Throws<ArgumentException>(() => b.EmitOverlay());
    }

    [Fact]
    public void EmitOverlay_RuntimeFlagWithQuote_IsEscaped_NotInjected()
    {
      // A double quote in a flag must be escaped inside the quoted scalar, never
      // allowed to terminate it (which could append arbitrary list items). The
      // emitted YAML is the real injection boundary, so assert directly on it.
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2").WithRuntimeFlags("--grammar", "a\"b"));

      var yaml = b.EmitOverlay();

      // The quote is escaped (\") and the scalar is rendered on a single list line, so
      // the raw quote never closes the scalar early to inject a new YAML item.
      Assert.Contains("- \"a\\\"b\"", yaml);
      Assert.DoesNotContain("- \"a\"", yaml);
    }

    [Fact]
    public void EmitOverlay_ValidInput_DoesNotThrow()
    {
      var b = new ComposeModelBuilder();
      b.AddModel("llm", m => m.WithModel("ai/smollm2:latest").WithContextSize(4096).WithRuntimeFlags("--temp", "0.7"));
      b.BindToService("app", "llm", endpointVar: "LLM_URL", modelVar: "LLM_MODEL");

      var yaml = b.EmitOverlay();
      Assert.Contains("    model: ai/smollm2:latest", yaml);
    }
  }
}
