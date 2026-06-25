using System;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for <see cref="InferenceModelId"/>: the verbatim inference-model-id
  /// value type that — unlike <see cref="ModelReference"/> — never injects
  /// <c>:latest</c>, so remote OpenAI ids (<c>gpt-4o-mini</c>) are sent unchanged.
  /// </summary>
  [Trait("Category", "Unit")]
  public class InferenceModelIdTests
  {
    [Theory]
    [InlineData("gpt-4o-mini")]
    [InlineData("ai/smollm2")]
    [InlineData("ai/smollm2:latest")]
    [InlineData("hf.co/org/repo:Q4_K_M")]
    [InlineData("text-embedding-3-small")]
    public void Constructor_PreservesValueVerbatim(string value)
    {
      var id = new InferenceModelId(value);

      Assert.Equal(value, id.Value);
      Assert.Equal(value, id.ToString());
      // Crucially: no ":latest" is ever appended.
      Assert.Equal(value, (string)id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_RejectsNullEmptyOrWhitespace(string value)
    {
      Assert.Throws<ArgumentException>(() => new InferenceModelId(value));
    }

    [Fact]
    public void ImplicitConversion_FromString_PreservesValue()
    {
      InferenceModelId id = "gpt-4o-mini";
      Assert.Equal("gpt-4o-mini", id.Value);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
      var a = new InferenceModelId("gpt-4o-mini");
      var b = new InferenceModelId("gpt-4o-mini");
      var c = new InferenceModelId("gpt-4o");

      Assert.Equal(a, b);
      Assert.True(a == b);
      Assert.False(a == c);
      Assert.True(a != c);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void FromModelReference_BareName_DropsAutoLatest()
    {
      var reference = ModelReference.Parse("ai/smollm2");

      // The Docker artifact form keeps :latest...
      Assert.Equal("ai/smollm2:latest", reference.ToString());

      // ...but the inference id must NOT.
      var id = InferenceModelId.FromModelReference(reference);
      Assert.NotNull(id);
      Assert.Equal("ai/smollm2", id.Value.ToString());
    }

    [Fact]
    public void FromModelReference_ExplicitLatest_DropsLatest()
    {
      // After parsing we cannot distinguish an explicit ":latest" from the default,
      // so both render as the bare id for inference (DMR treats them as equivalent).
      var id = InferenceModelId.FromModelReference(ModelReference.Parse("ai/smollm2:latest"));
      Assert.Equal("ai/smollm2", id.Value.ToString());
    }

    [Fact]
    public void FromModelReference_ExplicitTag_PreservesTag()
    {
      var id = InferenceModelId.FromModelReference(ModelReference.Parse("ai/smollm2:Q4_K_M"));
      Assert.Equal("ai/smollm2:Q4_K_M", id.Value.ToString());
    }

    [Fact]
    public void FromModelReference_RegistryQualified_DropsAutoLatest()
    {
      var id = InferenceModelId.FromModelReference(ModelReference.Parse("hf.co/org/repo"));
      Assert.Equal("hf.co/org/repo", id.Value.ToString());
    }

    [Fact]
    public void FromModelReference_Digest_PreservesDigest()
    {
      var reference = ModelReference.Parse("ai/smollm2@sha256:" + new string('a', 64));
      var id = InferenceModelId.FromModelReference(reference);
      Assert.Equal(reference.ToString(), id.Value.ToString());
    }

    [Fact]
    public void FromModelReference_Null_ReturnsNull()
    {
      Assert.Null(InferenceModelId.FromModelReference(null));
    }
  }
}
