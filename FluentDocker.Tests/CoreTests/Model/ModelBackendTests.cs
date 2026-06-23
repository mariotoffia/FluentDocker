using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for the open-value <see cref="ModelBackend"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelBackendTests
  {
    [Fact]
    public void Wellknown_HaveExpectedNames()
    {
      Assert.Equal("llama.cpp", ModelBackend.LlamaCpp.Name);
      Assert.Equal("vllm", ModelBackend.Vllm.Name);
      Assert.Equal("diffusers", ModelBackend.Diffusers.Name);
    }

    [Fact]
    public void Default_IsDefaultAndHasNullName()
    {
      var b = default(ModelBackend);
      Assert.True(b.IsDefault);
      Assert.Null(b.Name);
    }

    [Fact]
    public void WellKnown_AreNotDefault()
    {
      Assert.False(ModelBackend.LlamaCpp.IsDefault);
    }

    [Fact]
    public void Custom_CreatesNamedBackend()
    {
      var b = ModelBackend.Custom("my-engine");
      Assert.Equal("my-engine", b.Name);
      Assert.False(b.IsDefault);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
      Assert.Equal(ModelBackend.LlamaCpp, ModelBackend.Custom("llama.cpp"));
      Assert.True(ModelBackend.Vllm == ModelBackend.Custom("vllm"));
      Assert.True(ModelBackend.Vllm != ModelBackend.LlamaCpp);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
      Assert.Equal("llama.cpp", ModelBackend.LlamaCpp.ToString());
    }
  }
}
