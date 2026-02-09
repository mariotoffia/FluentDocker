using System;
using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Unit tests for PodmanCliManifestDriver argument building and JSON parsing.
  /// Uses reflection to test internal static methods.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliManifestDriverTests
  {
    #region BuildCreateArgs

    [Fact]
    public void BuildCreateArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new ManifestCreateConfig { Name = "mylist" };
      var result = InvokeBuildCreateArgs(config);
      Assert.Equal("manifest create mylist", result);
    }

    [Fact]
    public void BuildCreateArgs_WithImages_IncludesImages()
    {
      var config = new ManifestCreateConfig
      {
        Name = "mylist",
        Images = new List<string> { "alpine:latest", "nginx:latest" }
      };
      var result = InvokeBuildCreateArgs(config);
      Assert.Equal("manifest create mylist alpine:latest nginx:latest", result);
    }

    [Fact]
    public void BuildCreateArgs_WithAllAndAmend_IncludesFlags()
    {
      var config = new ManifestCreateConfig
      {
        Name = "mylist",
        All = true,
        Amend = true
      };
      var result = InvokeBuildCreateArgs(config);
      Assert.Contains("--all", result);
      Assert.Contains("--amend", result);
      Assert.Contains("mylist", result);
    }

    [Fact]
    public void BuildCreateArgs_WithAnnotations_IncludesAnnotations()
    {
      var config = new ManifestCreateConfig
      {
        Name = "mylist",
        Annotations = new Dictionary<string, string>
                {
                    { "org.opencontainers.image.author", "test" }
                }
      };
      var result = InvokeBuildCreateArgs(config);
      Assert.Contains("--annotation", result);
      Assert.Contains("org.opencontainers.image.author=test", result);
    }

    #endregion

    #region BuildAddArgs

    [Fact]
    public void BuildAddArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest"
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Equal("manifest add mylist alpine:latest", result);
    }

    [Fact]
    public void BuildAddArgs_WithArchOsVariant_IncludesFlags()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest",
        Arch = "arm64",
        Os = "linux",
        Variant = "v8"
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Contains("--arch arm64", result);
      Assert.Contains("--os linux", result);
      Assert.Contains("--variant v8", result);
    }

    [Fact]
    public void BuildAddArgs_WithAll_IncludesFlag()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest",
        All = true
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Contains("--all", result);
    }

    [Fact]
    public void BuildAddArgs_WithAnnotations_IncludesAnnotations()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest",
        Annotations = new Dictionary<string, string> { { "key", "value" } }
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Contains("--annotation", result);
      Assert.Contains("key=value", result);
    }

    [Fact]
    public void BuildAddArgs_WithOsVersion_IncludesFlag()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest",
        OsVersion = "10.0.17763.1"
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Contains("--os-version 10.0.17763.1", result);
    }

    [Fact]
    public void BuildAddArgs_WithFeatures_IncludesFlags()
    {
      var config = new ManifestAddConfig
      {
        ListName = "mylist",
        Image = "alpine:latest",
        Features = new List<string> { "sse4", "avx" }
      };
      var result = InvokeBuildAddArgs(config);
      Assert.Contains("--features sse4", result);
      Assert.Contains("--features avx", result);
    }

    #endregion

    #region BuildPushArgs

    [Fact]
    public void BuildPushArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "docker.io/myuser/mylist"
      };
      var result = InvokeBuildPushArgs(config);
      Assert.Contains("manifest push", result);
      Assert.Contains("--all", result); // All defaults to true
      Assert.Contains("mylist", result);
      Assert.Contains("docker.io/myuser/mylist", result);
    }

    [Fact]
    public void BuildPushArgs_WithRm_IncludesFlag()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "registry.example.com/mylist",
        Rm = true
      };
      var result = InvokeBuildPushArgs(config);
      Assert.Contains("--rm", result);
    }

    [Fact]
    public void BuildPushArgs_WithFormat_IncludesFlag()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "registry.example.com/mylist",
        Format = "v2s2"
      };
      var result = InvokeBuildPushArgs(config);
      Assert.Contains("--format v2s2", result);
    }

    [Fact]
    public void BuildPushArgs_TlsVerifyFalse_IncludesFlag()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "localhost:5000/mylist",
        TlsVerify = false
      };
      var result = InvokeBuildPushArgs(config);
      Assert.Contains("--tls-verify=false", result);
    }

    [Fact]
    public void BuildPushArgs_TlsVerifyTrue_IncludesFlag()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "registry.example.com/mylist",
        TlsVerify = true
      };
      var result = InvokeBuildPushArgs(config);
      Assert.Contains("--tls-verify=true", result);
    }

    [Fact]
    public void BuildPushArgs_AllFalse_OmitsFlag()
    {
      var config = new ManifestPushConfig
      {
        ListName = "mylist",
        Destination = "registry.example.com/mylist",
        All = false
      };
      var result = InvokeBuildPushArgs(config);
      Assert.DoesNotContain("--all", result);
    }

    #endregion

    #region BuildAnnotateArgs

    [Fact]
    public void BuildAnnotateArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new ManifestAnnotateConfig
      {
        ListName = "mylist",
        Image = "sha256:abc123"
      };
      var result = InvokeBuildAnnotateArgs(config);
      Assert.Equal("manifest annotate mylist sha256:abc123", result);
    }

    [Fact]
    public void BuildAnnotateArgs_WithArchOsVariant_IncludesFlags()
    {
      var config = new ManifestAnnotateConfig
      {
        ListName = "mylist",
        Image = "sha256:abc123",
        Arch = "amd64",
        Os = "linux",
        Variant = "v3"
      };
      var result = InvokeBuildAnnotateArgs(config);
      Assert.Contains("--arch amd64", result);
      Assert.Contains("--os linux", result);
      Assert.Contains("--variant v3", result);
    }

    [Fact]
    public void BuildAnnotateArgs_WithIndex_IncludesFlag()
    {
      var config = new ManifestAnnotateConfig
      {
        ListName = "mylist",
        Image = "sha256:abc123",
        IndexAnnotation = true,
        Annotations = new Dictionary<string, string> { { "key", "val" } }
      };
      var result = InvokeBuildAnnotateArgs(config);
      Assert.Contains("--index", result);
      Assert.Contains("--annotation", result);
    }

    [Fact]
    public void BuildAnnotateArgs_WithOsFeatures_IncludesFlags()
    {
      var config = new ManifestAnnotateConfig
      {
        ListName = "mylist",
        Image = "sha256:abc123",
        OsFeatures = new List<string> { "win32k" }
      };
      var result = InvokeBuildAnnotateArgs(config);
      Assert.Contains("--os-features win32k", result);
    }

    #endregion

    #region ParseManifestInspect

    [Fact]
    public void ParseManifestInspect_ValidJson_ReturnsResult()
    {
      var json = @"{
                ""schemaVersion"": 2,
                ""mediaType"": ""application/vnd.docker.distribution.manifest.list.v2+json"",
                ""manifests"": [
                    {
                        ""mediaType"": ""application/vnd.docker.container.image.v1+json"",
                        ""size"": 1234,
                        ""digest"": ""sha256:abc123"",
                        ""platform"": {
                            ""architecture"": ""amd64"",
                            ""os"": ""linux""
                        }
                    }
                ]
            }";

      var result = InvokeParseManifestInspect(json);
      Assert.Equal(2, result.SchemaVersion);
      Assert.NotNull(result.MediaType);
      Assert.Single(result.Manifests);
      Assert.Equal("sha256:abc123", result.Manifests[0].Digest);
      Assert.Equal(1234, result.Manifests[0].Size);
      Assert.Equal("amd64", result.Manifests[0].Platform.Architecture);
      Assert.Equal("linux", result.Manifests[0].Platform.Os);
    }

    [Fact]
    public void ParseManifestInspect_EmptyString_ReturnsEmpty()
    {
      var result = InvokeParseManifestInspect("");
      Assert.NotNull(result);
      Assert.Empty(result.Manifests);
    }

    [Fact]
    public void ParseManifestInspect_NullManifests_ReturnsEmpty()
    {
      var json = @"{ ""schemaVersion"": 2, ""manifests"": null }";
      var result = InvokeParseManifestInspect(json);
      Assert.Equal(2, result.SchemaVersion);
      Assert.Empty(result.Manifests);
    }

    [Fact]
    public void ParseManifestInspect_WithAnnotations_ParsesAnnotations()
    {
      var json = @"{
                ""schemaVersion"": 2,
                ""manifests"": [
                    {
                        ""digest"": ""sha256:def456"",
                        ""size"": 500,
                        ""platform"": { ""architecture"": ""arm64"", ""os"": ""linux"", ""variant"": ""v8"" },
                        ""annotations"": { ""org.opencontainers.image.ref.name"": ""latest"" }
                    }
                ]
            }";

      var result = InvokeParseManifestInspect(json);
      Assert.Single(result.Manifests);
      var entry = result.Manifests[0];
      Assert.Equal("arm64", entry.Platform.Architecture);
      Assert.Equal("v8", entry.Platform.Variant);
      Assert.Contains("org.opencontainers.image.ref.name", entry.Annotations.Keys);
      Assert.Equal("latest", entry.Annotations["org.opencontainers.image.ref.name"]);
    }

    [Fact]
    public void ParseManifestInspect_MultipleEntries_ParsesAll()
    {
      var json = @"{
                ""schemaVersion"": 2,
                ""manifests"": [
                    { ""digest"": ""sha256:aaa"", ""size"": 100, ""platform"": { ""architecture"": ""amd64"", ""os"": ""linux"" } },
                    { ""digest"": ""sha256:bbb"", ""size"": 200, ""platform"": { ""architecture"": ""arm64"", ""os"": ""linux"" } },
                    { ""digest"": ""sha256:ccc"", ""size"": 300, ""platform"": { ""architecture"": ""s390x"", ""os"": ""linux"" } }
                ]
            }";

      var result = InvokeParseManifestInspect(json);
      Assert.Equal(3, result.Manifests.Count);
      Assert.Equal("sha256:aaa", result.Manifests[0].Digest);
      Assert.Equal("sha256:bbb", result.Manifests[1].Digest);
      Assert.Equal("sha256:ccc", result.Manifests[2].Digest);
    }

    [Fact]
    public void ParseManifestInspect_WithPlatformFeatures_ParsesFeatures()
    {
      var json = @"{
                ""schemaVersion"": 2,
                ""manifests"": [
                    {
                        ""digest"": ""sha256:xyz"",
                        ""size"": 100,
                        ""platform"": {
                            ""architecture"": ""amd64"",
                            ""os"": ""linux"",
                            ""features"": [""sse4"", ""avx2""]
                        }
                    }
                ]
            }";

      var result = InvokeParseManifestInspect(json);
      Assert.Single(result.Manifests);
      Assert.Equal(2, result.Manifests[0].Platform.Features.Count);
      Assert.Contains("sse4", result.Manifests[0].Platform.Features);
      Assert.Contains("avx2", result.Manifests[0].Platform.Features);
    }

    #endregion

    #region Capabilities

    [Fact]
    public async void GetCapabilities_SupportsManifests()
    {
      var pack = new PodmanCliDriverPack();
      var caps = await pack.GetCapabilitiesAsync();
      Assert.True(caps.SupportsManifests);
    }

    #endregion

    #region Reflection Helpers

    private static string InvokeBuildCreateArgs(ManifestCreateConfig config)
    {
      var method = typeof(PodmanCliManifestDriver).GetMethod(
          "BuildCreateArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { config });
    }

    private static string InvokeBuildAddArgs(ManifestAddConfig config)
    {
      var method = typeof(PodmanCliManifestDriver).GetMethod(
          "BuildAddArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { config });
    }

    private static string InvokeBuildPushArgs(ManifestPushConfig config)
    {
      var method = typeof(PodmanCliManifestDriver).GetMethod(
          "BuildPushArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { config });
    }

    private static string InvokeBuildAnnotateArgs(ManifestAnnotateConfig config)
    {
      var method = typeof(PodmanCliManifestDriver).GetMethod(
          "BuildAnnotateArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { config });
    }

    private static ManifestInspectResult InvokeParseManifestInspect(string json)
    {
      var method = typeof(PodmanCliManifestDriver).GetMethod(
          "ParseManifestInspect",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (ManifestInspectResult)method.Invoke(null, new object[] { json });
    }

    #endregion
  }
}
