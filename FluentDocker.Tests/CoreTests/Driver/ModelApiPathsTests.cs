using System;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Security tests for <see cref="ModelApiPaths"/>: native <c>/models/*</c> paths are
  /// built from escaped, validated segments, and references that cannot be faithfully
  /// represented (registry / digest / specific tag) are refused rather than silently
  /// pointed at the wrong model.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelApiPathsTests
  {
    [Theory]
    [InlineData("ai/smollm2", "/models/ai/smollm2")]
    [InlineData("ai/smollm2:latest", "/models/ai/smollm2")]
    [InlineData("smollm2", "/models/smollm2")]
    public void ForModel_BuildsRepositoryPath(string reference, string expected)
    {
      Assert.Equal(expected, ModelApiPaths.ForModel(ModelReference.Parse(reference)));
    }

    [Fact]
    public void ForModel_RegistryQualified_NotSupported()
    {
      Assert.Throws<NotSupportedException>(() => ModelApiPaths.ForModel(ModelReference.Parse("hf.co/org/model")));
      Assert.Throws<NotSupportedException>(() => ModelApiPaths.ForModel(ModelReference.Parse("registry.io:5000/ns/model")));
    }

    [Fact]
    public void ForModel_SpecificTag_NotSupported()
    {
      Assert.Throws<NotSupportedException>(() => ModelApiPaths.ForModel(ModelReference.Parse("ai/smollm2:Q4_K_M")));
    }

    [Fact]
    public void ForModel_DigestPinned_NotSupported()
    {
      var reference = ModelReference.Parse("ai/smollm2@sha256:" + new string('a', 64));
      Assert.Throws<NotSupportedException>(() => ModelApiPaths.ForModel(reference));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    [InlineData("a%2fb")]
    [InlineData("a\tb")]
    [InlineData("a\nb")]
    [InlineData("ab")]
    [InlineData("")]
    public void IsSafeSegment_RejectsTraversalControlAndReservedChars(string segment)
    {
      Assert.False(ModelApiPaths.IsSafeSegment(segment));
    }

    [Theory]
    [InlineData("smollm2")]
    [InlineData("model.v2")]
    [InlineData("my-model_1")]
    public void IsSafeSegment_AllowsNormalNames(string segment)
    {
      Assert.True(ModelApiPaths.IsSafeSegment(segment));
    }
  }
}
