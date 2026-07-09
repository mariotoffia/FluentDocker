using System;
using System.Collections.Generic;
using System.IO;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public sealed class CommonProdReadyTests
  {
    [Fact]
    public void TryGetProperty_OnArrayRoot_ReturnsNull()
    {
      var value = JsonHelper.TryGetProperty("[1,2,3]", "x");

      Assert.Null(value);
    }

    [Fact]
    public void TryGetIntProperty_OnStringRoot_ReturnsNull()
    {
      var value = JsonHelper.TryGetIntProperty("\"hi\"", "x");

      Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_ContainerNetworkGlobalIpv6PrefixLenNull_Succeeds()
    {
      const string json = """
          {
            "Id": "abc",
            "NetworkSettings": {
              "Networks": {
                "bridge": {
                  "GlobalIPv6PrefixLen": null
                }
              }
            }
          }
          """;

      var ok = JsonHelper.TryDeserialize<Container>(json, out var container);

      Assert.True(ok);
      Assert.NotNull(container);
      var networkSettings = Assert.IsType<ContainerNetworkSettings>(container.NetworkSettings);
      var networks = Assert.IsType<Dictionary<string, BridgeNetwork>>(networkSettings.Networks);
      var network = Assert.Single(networks);
      Assert.Equal("bridge", network.Key);
      Assert.Equal(0, network.Value.GlobalIPv6PrefixLen);
    }

    [Fact]
    public void Copy_Directory_ReturnsExistingRelativeDirectory()
    {
      var root = Path.Combine(
          Directory.GetCurrentDirectory(),
          ".out",
          "common-prod-ready",
          Guid.NewGuid().ToString("N"));
      var source = Path.Combine(root, "source");
      var workdir = Path.Combine(root, "work");

      try
      {
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(workdir);
        File.WriteAllText(Path.Combine(source, "file.txt"), "content");

        var relative = new TemplateString(source).Copy(new TemplateString(workdir));

        var relativeName = Assert.IsType<string>(relative);
        Assert.Equal("source", relativeName);
        var copied = Path.Combine(workdir, relativeName);
        Assert.True(Directory.Exists(copied));
        Assert.Equal("content", File.ReadAllText(Path.Combine(copied, "file.txt")));
      }
      finally
      {
        if (Directory.Exists(root))
          Directory.Delete(root, recursive: true);
      }
    }

    [Fact]
    public void ComposeServiceDefinition_Volumes_CanBeSetWithObjectInitializer()
    {
      var volumes = new List<IServiceVolumeDefinition>
      {
        new ShortServiceVolumeDefinition { Entry = "./cache:/cache" }
      };

      var service = new ComposeServiceDefinition
      {
        Volumes = volumes
      };

      Assert.Same(volumes, service.Volumes);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void GetBoolOrDefault_Number_ReturnsNumericBoolean(int value, bool expected)
    {
      var element = JsonHelper.ParseElement($$"""{"enabled":{{value}}}""");

      Assert.Equal(expected, element.GetBoolOrDefault("enabled"));
    }
  }
}
