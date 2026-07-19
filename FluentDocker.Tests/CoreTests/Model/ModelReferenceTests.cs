using System;
using System.Text.Json;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for the <see cref="ModelReference"/> value object: the full
  /// parse / round-trip matrix from the DMR design (§7.1) including Docker Hub
  /// <c>ai/…</c>, Hugging Face <c>hf.co/…</c>, fully-qualified registries,
  /// digests, default tag resolution, and invalid inputs.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelReferenceTests
  {
    [Theory]
    // input, registry, namespace, name, tag
    [InlineData("ai/qwen3", null, "ai", "qwen3", "latest")]
    [InlineData("ai/qwen3:8b-q4", null, "ai", "qwen3", "8b-q4")]
    [InlineData("hf.co/bartowski/Llama-3.2", "hf.co", "bartowski", "Llama-3.2", "latest")]
    [InlineData("registry.io/team/m:v1", "registry.io", "team", "m", "v1")]
    [InlineData("ai/smollm2:latest", null, "ai", "smollm2", "latest")]
    [InlineData("hf.co/org/repo:Q4_K_M", "hf.co", "org", "repo", "Q4_K_M")]
    public void Parse_DecomposesReference(string input, string? registry, string ns, string name, string tag)
    {
      var model = ModelReference.Parse(input);

      Assert.Equal(registry, model.Registry);
      Assert.Equal(ns, model.Namespace);
      Assert.Equal(name, model.Name);
      Assert.Equal(tag, model.Tag);
      Assert.Null(model.Digest);
    }

    [Fact]
    public void Parse_RegistryWithPort_IsTreatedAsRegistry()
    {
      var model = ModelReference.Parse("registry.io:5000/team/m:v1");

      Assert.Equal("registry.io:5000", model.Registry);
      Assert.Equal("team", model.Namespace);
      Assert.Equal("m", model.Name);
      Assert.Equal("v1", model.Tag);
    }

    [Theory]
    [InlineData("[::1]:5000/ns/name", "[::1]:5000")]
    [InlineData("[fe80::1]:443/ns/name", "[fe80::1]:443")]
    public void Parse_BracketedIpv6RegistryWithPort_IsTreatedAsRegistry(string reference, string registry)
    {
      var model = ModelReference.Parse(reference);

      Assert.Equal(registry, model.Registry);
      Assert.Equal("ns", model.Namespace);
      Assert.Equal("name", model.Name);
    }

    [Fact]
    public void Parse_BareName_HasNullNamespace()
    {
      var model = ModelReference.Parse("smollm2");

      Assert.Null(model.Registry);
      Assert.Null(model.Namespace);
      Assert.Equal("smollm2", model.Name);
      Assert.Equal("latest", model.Tag);
    }

    [Fact]
    public void Parse_WithDigest_CapturesDigestAndNullTag()
    {
      var model = ModelReference.Parse("ai/qwen3@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

      Assert.Equal("ai", model.Namespace);
      Assert.Equal("qwen3", model.Name);
      Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", model.Digest);
      Assert.Null(model.Tag);
    }

    [Fact]
    public void Parse_WithTagAndDigest_CapturesBoth()
    {
      var model = ModelReference.Parse("ai/qwen3:8b-q4@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

      Assert.Equal("qwen3", model.Name);
      Assert.Equal("8b-q4", model.Tag);
      Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", model.Digest);
    }

    [Theory]
    [InlineData("hf.co/org/repo", true)]
    [InlineData("HF.CO/org/repo", true)]
    [InlineData("ai/qwen3", false)]
    [InlineData("registry.io/team/m", false)]
    public void IsHuggingFace_DetectedFromRegistry(string input, bool expected)
    {
      Assert.Equal(expected, ModelReference.Parse(input).IsHuggingFace);
    }

    [Theory]
    [InlineData("ai/qwen3", "ai/qwen3:latest")]
    [InlineData("ai/qwen3:8b-q4", "ai/qwen3:8b-q4")]
    [InlineData("hf.co/bartowski/Llama-3.2", "hf.co/bartowski/Llama-3.2:latest")]
    [InlineData("registry.io/team/m:v1", "registry.io/team/m:v1")]
    public void ToString_RendersCanonicalForm(string input, string expected)
    {
      Assert.Equal(expected, ModelReference.Parse(input).ToString());
    }

    [Fact]
    public void ToString_WithDigest_OmitsDefaultTag()
    {
      Assert.Equal(
          "ai/qwen3@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          ModelReference.Parse("ai/qwen3@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").ToString());
    }

    [Theory]
    [InlineData("ai/qwen3")]
    [InlineData("hf.co/org/repo:Q4_K_M")]
    [InlineData("registry.io:5000/team/m:v1")]
    [InlineData("ai/qwen3@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ToString_RoundTripsThroughParse(string input)
    {
      var first = ModelReference.Parse(input);
      var second = ModelReference.Parse(first.ToString());

      Assert.Equal(first, second);
      Assert.Equal(first.ToString(), second.ToString());
    }

    [Fact]
    public void Equality_IsValueBased_RegistryCaseInsensitive()
    {
      var a = ModelReference.Parse("HF.CO/org/repo:latest");
      var b = ModelReference.Parse("hf.co/org/repo");

      Assert.Equal(a, b);
      Assert.True(a == b);
      Assert.False(a != b);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentTag_NotEqual()
    {
      Assert.NotEqual(ModelReference.Parse("ai/qwen3:a"), ModelReference.Parse("ai/qwen3:b"));
    }

    [Fact]
    // ROB-2: references differing only in registry case are Equals-equal AND must
    // serialize identically (the registry is normalized to lowercase on construction).
    public void EqualReferences_DifferingOnlyInRegistryCase_SerializeIdentically()
    {
      var a = ModelReference.Parse("MyReg.com/ns/x:v1");
      var b = ModelReference.Parse("myreg.com/ns/x:v1");

      Assert.Equal(a, b);
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
      Assert.Equal(a.ToString(), b.ToString());
      Assert.Equal("myreg.com/ns/x:v1", a.ToString());
    }

    [Fact]
    // ROB-2: only the registry host is lowercased; namespace/name/tag keep their case.
    public void ToString_NormalizesRegistryToLowercase_PreservingRepositoryAndTagCase()
    {
      var model = ModelReference.Parse("MyReg.COM/Ns/Repo-X:Q4_K_M");

      Assert.Equal("myreg.com", model.Registry);
      Assert.Equal("Ns", model.Namespace);
      Assert.Equal("Repo-X", model.Name);
      Assert.Equal("Q4_K_M", model.Tag);
      Assert.Equal("myreg.com/Ns/Repo-X:Q4_K_M", model.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_NullOrEmpty_ReturnsFalse(string? input)
    {
      Assert.False(ModelReference.TryParse(input!, out var model)); // intentional null to verify null-handling
      Assert.Null(model);
    }

    [Theory]
    [InlineData("ai/")]
    [InlineData("/qwen3")]
    [InlineData("ai//qwen3")]
    [InlineData("ai/qwen3:")]
    [InlineData("ai/qwen3@")]
    [InlineData("ai/qwen3@d1@d2")]
    [InlineData(":latest")]
    [InlineData("ai/ qwen3")]
    // SF-2: digest must be a structurally valid algo:hex pair
    [InlineData("ai/qwen3@garbage")]
    [InlineData("ai/qwen3@sha256:")]
    [InlineData("ai/qwen3@:abc123")]
    [InlineData("ai/qwen3@sha256:xyz")]
    [InlineData("ai/qwen3@sha256:abc123")]
    [InlineData("ai/qwen3@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ai/qwen3@sha512:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ai/qwen3@SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("ai/qwen3@sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ubuntu:latest/foo")]
    [InlineData("ai/-qwen3")]
    [InlineData("ai/qwen3/-bad")]
    [InlineData("ai/qwen3:bad tag")]
    [InlineData("ai/qwen3:bad@tag")]
    public void TryParse_Malformed_ReturnsFalse(string input)
    {
      Assert.False(ModelReference.TryParse(input, out var model));
      Assert.Null(model);
    }

    [Fact]
    public void Parse_WithSha512Digest_CapturesDigest()
    {
      var digest =
          "sha512:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
      var model = ModelReference.Parse("ai/qwen3@" + digest);

      Assert.Equal(digest, model.Digest);
    }

    [Theory]
    // NTH-1: registry + bare name (no namespace)
    [InlineData("registry.io/m", "registry.io", null, "m", "latest")]
    [InlineData("localhost:5000/m:v2", "localhost:5000", null, "m", "v2")]
    public void Parse_RegistryWithBareName_HasNullNamespace(string input, string registry, string? ns, string name, string tag)
    {
      var model = ModelReference.Parse(input);

      Assert.Equal(registry, model.Registry);
      Assert.Equal(ns, model.Namespace);
      Assert.Equal(name, model.Name);
      Assert.Equal(tag, model.Tag);
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrueWithModel()
    {
      Assert.True(ModelReference.TryParse("ai/qwen3", out var model));
      Assert.Equal("qwen3", model.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_NullOrEmpty_Throws(string? input)
    {
      Assert.Throws<ArgumentException>(() => ModelReference.Parse(input!)); // intentional null to verify null-handling
    }

    [Fact]
    public void Parse_Malformed_ThrowsFormatException()
    {
      Assert.Throws<FormatException>(() => ModelReference.Parse("ai//qwen3"));
    }

    [Fact]
    public void JsonConverter_WhitespaceString_DeserializesLikeEmptyString()
    {
      Assert.Null(JsonSerializer.Deserialize<ModelReference>("\"   \""));
    }
  }
}
