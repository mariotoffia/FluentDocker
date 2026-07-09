#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentDocker.Builders.Compose;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using FluentDocker.Model.Networks;
using FluentDocker.Model.Volumes;
using FluentDocker.Resources;
using Xunit;

namespace FluentDocker.Tests.CoreTests
{
  [Trait("Category", "Unit")]
  public sealed class Chunk8EndpointAndParsingTests
  {
    [Fact]
    public void RawAuthorityOnlyEndpoint_UsesSameEnginePathAsCustom()
    {
      var raw = ModelRunnerEndpoint.Raw(new Uri("http://localhost:12434"));
      var custom = ModelRunnerEndpoint.Custom(new Uri("http://localhost:12434"));

      Assert.Equal(custom.EnginePath, raw.EnginePath);
      Assert.Equal(custom.EngineV1Path("/chat/completions"), raw.EngineV1Path("/chat/completions"));
    }

    [Fact]
    public void ModelRunnerEndpoint_RejectsBlankEngineNames()
    {
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.Custom(new Uri("http://localhost:12434"), ""));
      Assert.Throws<ArgumentException>(() => ModelRunnerEndpoint.Raw(new Uri("http://localhost:12434/v1"), " "));
    }

    [Fact]
    public void DockerUri_RoundTripsCanonicalAndLegacyNpipeDaemonUris()
    {
      var canonical = new DockerUri("npipe:////./pipe/docker_engine");
      var legacy = new DockerUri("npipe://./pipe/docker_engine");

      Assert.True(canonical.IsStandardDaemon);
      Assert.Equal("npipe:////./pipe/docker_engine", canonical.ToString());
      Assert.True(legacy.IsStandardDaemon);
      Assert.Equal("npipe:////./pipe/docker_engine", legacy.ToString());
    }

    [Fact]
    public void JsonHelperTryGetProperty_ReturnsNullForNonStringProperty()
    {
      var value = JsonHelper.TryGetProperty("{\"a\":5}", "a");

      Assert.Null(value);
    }

    [Fact]
    public void ComposeModelEnvName_RejectsTrailingNewline()
    {
      var builder = new ComposeModelBuilder();
      builder.BindToService("svc", "llm", "LLM_URL\n");

      Assert.Throws<ArgumentException>(() => builder.EmitOverlay());
    }

    [Fact]
    public void ShellArgParser_RejectsUnterminatedQuotes()
    {
      Assert.Throws<FormatException>(() => ShellArgParser.Parse("echo 'abc"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LenientStringDictionaryConverter_TrimsPairsAndKeepsUnsplittableRemainderInValue()
    {
      var volume = JsonSerializer.Deserialize<Volume>("{\"Labels\":\"a = b,bare\"}", JsonHelper.CaseInsensitiveOptions)!;

      Assert.Equal("b,bare", volume.Labels!["a"]);
      Assert.DoesNotContain("bare", volume.Labels.Keys);
    }
  }

  [Trait("Category", "Unit")]
  public sealed class Chunk8ModelReferenceTests
  {
    [Theory]
    [InlineData("bad!host.com/ns/name:tag")]
    [InlineData("foo.com:99999999/name")]
    [InlineData("example.com:0/x")]
    public void ModelReferenceTryParse_RejectsInvalidRegistryHostsAndPorts(string reference)
    {
      Assert.False(ModelReference.TryParse(reference, out _));
    }

    [Fact]
    public void ModelReference_NormalizesDockerHubRegistryAliasesForEquality()
    {
      var bare = ModelReference.Parse("ai/smollm2");

      Assert.Equal(bare, ModelReference.Parse("index.docker.io/ai/smollm2"));
      Assert.Equal(bare, ModelReference.Parse("registry-1.docker.io/ai/smollm2"));
    }

    [Fact]
    public void ModelReference_RejectsDigestOnlySha256Reference()
    {
      var digest = new string('a', 64);

      Assert.False(ModelReference.TryParse("sha256:" + digest, out _));
    }

    [Fact]
    public void ModelReferenceJsonConverter_ThrowsJsonExceptionForNonStringToken()
    {
      Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ModelReference>("5", JsonHelper.DefaultOptions));
    }
  }

  [Trait("Category", "Unit")]
  public sealed class Chunk8DtoAndOptionTests
  {
    [Fact]
    public void ContainerNetworkSettingsPorts_AllowsNullEndpointArrays()
    {
      var settings = JsonHelper.TryDeserialize<ContainerNetworkSettings>("{\"Ports\":{\"80/tcp\":null}}")!;
      var portsNullability = NullabilityOf<ContainerNetworkSettings>(nameof(ContainerNetworkSettings.Ports));

      Assert.Null(settings.Ports!["80/tcp"]);
      Assert.Equal(NullabilityState.Nullable, portsNullability.GenericTypeArguments[1].ReadState);
    }

    [Theory]
    [InlineData(typeof(Volume), nameof(Volume.Driver))]
    [InlineData(typeof(Volume), nameof(Volume.Name))]
    [InlineData(typeof(DockerImageRowResponse), nameof(DockerImageRowResponse.Id))]
    [InlineData(typeof(DockerImageRowResponse), nameof(DockerImageRowResponse.Name))]
    [InlineData(typeof(DockerImageRowResponse), nameof(DockerImageRowResponse.Tags))]
    [InlineData(typeof(NetworkedContainer), nameof(NetworkedContainer.Name))]
    public void EngineOmittableDtoMembers_AreAnnotatedNullable(Type type, string propertyName)
    {
      Assert.Equal(NullabilityState.Nullable, NullabilityOf(type, propertyName).WriteState);
    }

    [Fact]
    public void InferenceModelIdDefaultValue_ReturnsEmptyValue()
    {
      Assert.Equal(string.Empty, default(InferenceModelId).Value);
    }

    [Fact]
    public void ChatCompletionRequestSeed_AcceptsUint32Range()
    {
      var request = new ChatCompletionRequest { Seed = 4294967295L };

      var json = JsonSerializer.Serialize(request, JsonHelper.DefaultOptions);

      Assert.Contains("4294967295", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelConfigureOptions_RejectsInvalidContextSizeAndResetConflict()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new ModelConfigureOptions { ContextSize = 0 });
      Assert.Throws<ArgumentException>(() => new ModelConfigureOptions { ContextSize = 4096, ResetContextSize = true });
      Assert.Throws<ArgumentException>(() => new ModelConfigureOptions { ResetContextSize = true, ContextSize = 4096 });
    }

    [Fact]
    public void ModelRunnerInstallOptions_RejectsUnknownGpuMode()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new ModelRunnerInstallOptions { Gpu = "metal" });
      Assert.Equal("cuda", new ModelRunnerInstallOptions { Gpu = "cuda" }.Gpu);
    }

    private static NullabilityInfo NullabilityOf<T>(string propertyName) =>
        NullabilityOf(typeof(T), propertyName);

    private static NullabilityInfo NullabilityOf(Type type, string propertyName) =>
        new NullabilityInfoContext().Create(type.GetProperty(propertyName)!);
  }

  [Trait("Category", "Unit")]
  public sealed class Chunk8ResourceAndErrorContextTests
  {
    [Fact]
    public void ResourceQueryWithoutNamespace_ThrowsFluentDockerException()
    {
      var ex = Assert.Throws<FluentDockerException>(() => new ResourceQuery().Query().GetEnumerator().MoveNext());

      Assert.Contains("Namespace not set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedResourceAssemblyLookup_IsCaseInsensitiveAndThrowsTypedException()
    {
      var assembly = typeof(Chunk8ResourceAndErrorContextTests).Assembly.GetName().Name!.ToLowerInvariant();
      var resource = new EmbeddedUri($"emb:{assembly}/No.Such/missing.txt");

      var ex = Assert.Throws<FluentDockerException>(() => resource.ToFile(".out/chunk8-resource"));

      Assert.Contains("Manifest resource", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnrichContext_CreatesContextForFailedResponsesWithoutOne()
    {
      var response = CommandResponse<int>.Fail("error", "ERR_001");

      var enriched = response.EnrichContext(ctx => ctx.WithDriverId("docker"));

      Assert.NotNull(enriched.ErrorContext);
      Assert.Equal("docker", enriched.ErrorContext.DriverId);
    }

    [Fact]
    public void DirectoryHelperDeleteDirectory_DoesNotThrowWhenDirectoryIsAlreadyGone()
    {
      // ponytail: deterministic TOCTOU needs a filesystem hook; this smoke covers the not-found outcome.
      var path = Path.Combine(".out", "chunk8-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(path);
      Directory.Delete(path);

      DirectoryHelper.DeleteDirectory(path, false);
    }
  }
}
