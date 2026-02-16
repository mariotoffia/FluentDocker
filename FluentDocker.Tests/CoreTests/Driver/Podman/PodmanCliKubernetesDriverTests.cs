using System.Collections.Generic;
using System.Reflection;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  public class PodmanCliKubernetesDriverTests
  {
    #region BuildPlayArgs Tests

    [Fact]
    public void BuildPlayArgs_MinimalConfig_ReturnsBasicCommand()
    {
      var config = new KubePlayConfig { YamlPath = "/tmp/pod.yaml" };
      var result = InvokeBuildPlayArgs(config);
      Assert.Equal("kube play /tmp/pod.yaml", result);
    }

    [Fact]
    public void BuildPlayArgs_FullConfig_IncludesAllFlags()
    {
      var config = new KubePlayConfig
      {
        YamlPath = "/tmp/app.yaml",
        Network = "my-net",
        ConfigMaps = new List<string> { "/tmp/cm1.yaml", "/tmp/cm2.yaml" },
        LogDriver = "journald",
        Replace = true,
        Start = false,
        Annotations = new Dictionary<string, string>
                {
                    { "env", "test" },
                    { "team", "infra" }
                }
      };

      var result = InvokeBuildPlayArgs(config);

      Assert.Contains("--network my-net", result);
      Assert.Contains("--configmap /tmp/cm1.yaml", result);
      Assert.Contains("--configmap /tmp/cm2.yaml", result);
      Assert.Contains("--log-driver journald", result);
      Assert.Contains("--replace", result);
      Assert.Contains("--start=false", result);
      Assert.Contains("--annotation env=test", result);
      Assert.Contains("--annotation team=infra", result);
      Assert.Contains("/tmp/app.yaml", result);
      Assert.StartsWith("kube play", result);
    }

    [Fact]
    public void BuildPlayArgs_WithReplace_AddsReplaceFlag()
    {
      var config = new KubePlayConfig
      {
        YamlPath = "/tmp/pod.yaml",
        Replace = true
      };

      var result = InvokeBuildPlayArgs(config);
      Assert.Contains("--replace", result);
    }

    [Fact]
    public void BuildPlayArgs_WithNoStart_AddsStartFalse()
    {
      var config = new KubePlayConfig
      {
        YamlPath = "/tmp/pod.yaml",
        Start = false
      };

      var result = InvokeBuildPlayArgs(config);
      Assert.Contains("--start=false", result);
    }

    [Fact]
    public void BuildPlayArgs_StartTrue_OmitsStartFlag()
    {
      var config = new KubePlayConfig
      {
        YamlPath = "/tmp/pod.yaml",
        Start = true
      };

      var result = InvokeBuildPlayArgs(config);
      Assert.DoesNotContain("--start", result);
    }

    [Fact]
    public void BuildPlayArgs_NoNetwork_OmitsNetworkFlag()
    {
      var config = new KubePlayConfig { YamlPath = "/tmp/pod.yaml" };
      var result = InvokeBuildPlayArgs(config);
      Assert.DoesNotContain("--network", result);
    }

    #endregion

    #region ParsePlayOutput Tests

    [Fact]
    public void ParsePlayOutput_EmptyString_ReturnsEmpty()
    {
      var result = InvokeParsePlayOutput("");
      Assert.NotNull(result);
      Assert.Empty(result.Pods);
    }

    [Fact]
    public void ParsePlayOutput_Null_ReturnsEmpty()
    {
      var result = InvokeParsePlayOutput(null);
      Assert.NotNull(result);
      Assert.Empty(result.Pods);
    }

    [Fact]
    public void ParsePlayOutput_Whitespace_ReturnsEmpty()
    {
      var result = InvokeParsePlayOutput("   ");
      Assert.NotNull(result);
      Assert.Empty(result.Pods);
    }

    [Fact]
    public void ParsePlayOutput_JsonWithPods_ReturnsPods()
    {
      var json = @"{
                ""Pods"": [
                    {
                        ""ID"": ""abc123def456"",
                        ""Containers"": [""c1aaa"", ""c2bbb""]
                    },
                    {
                        ""ID"": ""xyz789"",
                        ""Containers"": [""c3ccc""]
                    }
                ]
            }";

      var result = InvokeParsePlayOutput(json);

      Assert.Equal(2, result.Pods.Count);
      Assert.Equal("abc123def456", result.Pods[0].Id);
      Assert.Equal(2, result.Pods[0].Containers.Count);
      Assert.Equal("c1aaa", result.Pods[0].Containers[0]);
      Assert.Equal("c2bbb", result.Pods[0].Containers[1]);
      Assert.Equal("xyz789", result.Pods[1].Id);
      Assert.Single(result.Pods[1].Containers);
    }

    [Fact]
    public void ParsePlayOutput_JsonArray_ReturnsPods()
    {
      var json = @"[
                {
                    ""ID"": ""pod1"",
                    ""Containers"": [""c1""]
                }
            ]";

      var result = InvokeParsePlayOutput(json);

      Assert.Single(result.Pods);
      Assert.Equal("pod1", result.Pods[0].Id);
      Assert.Single(result.Pods[0].Containers);
    }

    [Fact]
    public void ParsePlayOutput_JsonContainerObjects_ReturnsPods()
    {
      var json = @"{
                ""Pods"": [{
                    ""ID"": ""mypod"",
                    ""Containers"": [
                        {""ID"": ""aaa111""},
                        {""Id"": ""bbb222""}
                    ]
                }]
            }";

      var result = InvokeParsePlayOutput(json);

      Assert.Single(result.Pods);
      Assert.Equal(2, result.Pods[0].Containers.Count);
      Assert.Equal("aaa111", result.Pods[0].Containers[0]);
      Assert.Equal("bbb222", result.Pods[0].Containers[1]);
    }

    [Fact]
    public void ParsePlayOutput_LineBasedPodContainer_ReturnsPods()
    {
      var output = "Pod:\nabc123def456abc1\nContainer:\nc1aaa111bbb222cc\nContainer:\nc2ddd333eee444ff\n";

      var result = InvokeParsePlayOutput(output);

      Assert.Single(result.Pods);
      Assert.Equal("abc123def456abc1", result.Pods[0].Id);
      Assert.Equal(2, result.Pods[0].Containers.Count);
      Assert.Equal("c1aaa111bbb222cc", result.Pods[0].Containers[0]);
      Assert.Equal("c2ddd333eee444ff", result.Pods[0].Containers[1]);
    }

    [Fact]
    public void ParsePlayOutput_BareHexIds_ReturnsPods()
    {
      var output = "abc123def456abc1\nc1aaa111bbb222cc\n";

      var result = InvokeParsePlayOutput(output);

      Assert.Single(result.Pods);
      Assert.Equal("abc123def456abc1", result.Pods[0].Id);
      Assert.Single(result.Pods[0].Containers);
      Assert.Equal("c1aaa111bbb222cc", result.Pods[0].Containers[0]);
    }

    #endregion

    #region Capabilities Tests

    [Fact]
    public async Task GetCapabilities_SupportsKubernetes()
    {
      var pack = new PodmanCliDriverPack();
      var caps = await pack.GetCapabilitiesAsync(TestContext.Current.CancellationToken);

      Assert.True(caps.SupportsKubernetes);
    }

    #endregion

    #region Reflection Helpers

    private static string InvokeBuildPlayArgs(KubePlayConfig config)
    {
      var method = typeof(PodmanCliKubernetesDriver).GetMethod(
          "BuildPlayArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, new object[] { config });
    }

    private static KubePlayResult InvokeParsePlayOutput(string output)
    {
      var method = typeof(PodmanCliKubernetesDriver).GetMethod(
          "ParsePlayOutput",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (KubePlayResult)method.Invoke(null, new object[] { output });
    }

    #endregion
  }
}
