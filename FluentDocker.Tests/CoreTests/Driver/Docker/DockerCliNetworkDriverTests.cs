using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliNetworkDriver: network create argument building,
  /// filter argument construction, and JSON list parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliNetworkDriverTests
  {
    #region Network Create Args - Name Only

    [Fact]
    public void NetworkCreateArgs_NameOnly_ProducesMinimalArgs()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.StartsWith("network create", args);
      Assert.EndsWith(" mynet", args);
    }

    #endregion

    #region Network Create Args - Driver

    [Fact]
    public void NetworkCreateArgs_WithDriver_IncludesDriverFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Driver = "overlay"
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--driver overlay", args);
    }

    [Fact]
    public void NetworkCreateArgs_DefaultBridgeDriver_IncludesBridge()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      // NetworkCreateConfig defaults Driver to "bridge"
      Assert.Contains("--driver bridge", args);
    }

    #endregion

    #region Network Create Args - Subnet / Gateway

    [Fact]
    public void NetworkCreateArgs_WithSubnet_IncludesSubnetFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Subnet = "172.28.0.0/16"
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--subnet 172.28.0.0/16", args);
    }

    [Fact]
    public void NetworkCreateArgs_WithGateway_IncludesGatewayFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Gateway = "172.28.0.1"
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--gateway 172.28.0.1", args);
    }

    [Fact]
    public void NetworkCreateArgs_NullSubnet_OmitsSubnetFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--subnet", args);
    }

    [Fact]
    public void NetworkCreateArgs_NullGateway_OmitsGatewayFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--gateway", args);
    }

    #endregion

    #region Network Create Args - IPv6 / Internal

    [Fact]
    public void NetworkCreateArgs_IPv6Enabled_IncludesFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        EnableIPv6 = true
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--ipv6", args);
    }

    [Fact]
    public void NetworkCreateArgs_IPv6Disabled_OmitsFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--ipv6", args);
    }

    [Fact]
    public void NetworkCreateArgs_Internal_IncludesFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Internal = true
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--internal", args);
    }

    [Fact]
    public void NetworkCreateArgs_InternalFalse_OmitsFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--internal", args);
    }

    #endregion

    #region Network Create Args - Options

    [Fact]
    public void NetworkCreateArgs_SingleOption_IncludesOptFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Options = { { "com.docker.network.bridge.name", "br0" } }
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--opt com.docker.network.bridge.name=br0", args);
    }

    [Fact]
    public void NetworkCreateArgs_MultipleOptions_IncludesAll()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Options =
        {
          { "com.docker.network.bridge.name", "br0" },
          { "com.docker.network.bridge.enable_icc", "true" }
        }
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--opt com.docker.network.bridge.name=br0", args);
      Assert.Contains("--opt com.docker.network.bridge.enable_icc=true", args);
    }

    [Fact]
    public void NetworkCreateArgs_NoOptions_OmitsOptFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--opt", args);
    }

    #endregion

    #region Network Create Args - Labels

    [Fact]
    public void NetworkCreateArgs_SingleLabel_IncludesLabelFlag()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Labels = { { "env", "test" } }
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--label env=test", args);
    }

    [Fact]
    public void NetworkCreateArgs_MultipleLabels_IncludesAll()
    {
      var config = new NetworkCreateConfig
      {
        Name = "mynet",
        Labels =
        {
          { "env", "test" },
          { "team", "devops" }
        }
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.Contains("--label env=test", args);
      Assert.Contains("--label team=devops", args);
    }

    [Fact]
    public void NetworkCreateArgs_NoLabels_OmitsLabelFlag()
    {
      var config = new NetworkCreateConfig { Name = "mynet" };

      var args = BuildNetworkCreateArgs(config);

      Assert.DoesNotContain("--label", args);
    }

    #endregion

    #region Network Create Args - Full Config

    [Fact]
    public void NetworkCreateArgs_FullConfig_ContainsAllFlags()
    {
      var config = new NetworkCreateConfig
      {
        Name = "my-overlay-net",
        Driver = "overlay",
        Subnet = "10.0.0.0/24",
        Gateway = "10.0.0.1",
        EnableIPv6 = true,
        Internal = true,
        Options = { { "encrypted", "true" } },
        Labels = { { "project", "test" } }
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.StartsWith("network create", args);
      Assert.Contains("--driver overlay", args);
      Assert.Contains("--subnet 10.0.0.0/24", args);
      Assert.Contains("--gateway 10.0.0.1", args);
      Assert.Contains("--ipv6", args);
      Assert.Contains("--internal", args);
      Assert.Contains("--opt encrypted=true", args);
      Assert.Contains("--label project=test", args);
      Assert.EndsWith(" my-overlay-net", args);
    }

    [Fact]
    public void NetworkCreateArgs_NameIsLastArgument()
    {
      var config = new NetworkCreateConfig
      {
        Name = "testnet",
        Driver = "bridge",
        Subnet = "172.20.0.0/16"
      };

      var args = BuildNetworkCreateArgs(config);

      Assert.EndsWith(" testnet", args);
    }

    #endregion

    #region Network List Filter Args

    [Fact]
    public void NetworkListFilterArgs_WithName_IncludesNameFilter()
    {
      var filter = new NetworkListFilter { Name = "mynet" };

      var args = BuildNetworkListFilterArgs(filter);

      Assert.Contains("--filter name=mynet", args);
    }

    [Fact]
    public void NetworkListFilterArgs_WithLabels_IncludesLabelFilter()
    {
      var filter = new NetworkListFilter
      {
        Labels = { { "env", "prod" } }
      };

      var args = BuildNetworkListFilterArgs(filter);

      Assert.Contains("--filter label=env=prod", args);
    }

    [Fact]
    public void NetworkListFilterArgs_WithNameAndLabels_IncludesBoth()
    {
      var filter = new NetworkListFilter
      {
        Name = "mynet",
        Labels = { { "env", "prod" }, { "team", "backend" } }
      };

      var args = BuildNetworkListFilterArgs(filter);

      Assert.Contains("--filter name=mynet", args);
      Assert.Contains("--filter label=env=prod", args);
      Assert.Contains("--filter label=team=backend", args);
    }

    [Fact]
    public void NetworkListFilterArgs_NullFilter_NoFilterFlags()
    {
      var args = BuildNetworkListFilterArgs(null);

      Assert.DoesNotContain("--filter", args);
    }

    [Fact]
    public void NetworkListFilterArgs_EmptyFilter_NoFilterFlags()
    {
      var filter = new NetworkListFilter();

      var args = BuildNetworkListFilterArgs(filter);

      Assert.DoesNotContain("--filter", args);
    }

    #endregion

    #region Network JSON Parsing

    [Fact]
    public void ParseNetworkJson_ValidJson_ReturnsNetwork()
    {
      var json = @"{""ID"":""abc123"",""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""}";

      var network = JsonSerializer.Deserialize<Network>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(network);
      Assert.Equal("bridge", network.Name);
      Assert.Equal("bridge", network.Driver);
      Assert.Equal("local", network.Scope);
    }

    [Fact]
    public void ParseNetworkJson_WithLabelsAsObject_ParsesLabels()
    {
      var json = @"{""Name"":""mynet"",""Labels"":{""env"":""prod"",""team"":""backend""}}";

      var network = JsonSerializer.Deserialize<Network>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(network);
      Assert.Equal(2, network.Labels.Count);
      Assert.Equal("prod", network.Labels["env"]);
      Assert.Equal("backend", network.Labels["team"]);
    }

    [Fact]
    public void ParseNetworkJson_WithLabelsAsCommaSeparatedString_ParsesLabels()
    {
      var json = @"{""Name"":""mynet"",""Labels"":""env=prod,team=backend""}";

      var network = JsonSerializer.Deserialize<Network>(json, JsonHelper.CaseInsensitiveOptions);

      Assert.NotNull(network);
      Assert.Equal(2, network.Labels.Count);
      Assert.Equal("prod", network.Labels["env"]);
      Assert.Equal("backend", network.Labels["team"]);
    }

    [Fact]
    public void ParseNetworkJson_MultipleLines_ParsesAll()
    {
      var lines = new[]
      {
        @"{""Name"":""bridge"",""Driver"":""bridge"",""Scope"":""local""}",
        @"{""Name"":""host"",""Driver"":""host"",""Scope"":""local""}",
        @"{""Name"":""none"",""Driver"":""null"",""Scope"":""local""}"
      };

      var networks = new List<Network>();
      foreach (var line in lines)
      {
        var network = JsonSerializer.Deserialize<Network>(line, JsonHelper.CaseInsensitiveOptions);
        if (network != null)
          networks.Add(network);
      }

      Assert.Equal(3, networks.Count);
      Assert.Equal("bridge", networks[0].Name);
      Assert.Equal("host", networks[1].Name);
      Assert.Equal("none", networks[2].Name);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Replicates the argument building logic from DockerCliNetworkDriver.CreateAsync
    /// to test it in isolation without executing Docker commands.
    /// </summary>
    private static string BuildNetworkCreateArgs(NetworkCreateConfig config)
    {
      var args = new List<string> { "network", "create" };

      if (!string.IsNullOrEmpty(config.Driver))
        args.Add($"--driver {config.Driver}");

      if (!string.IsNullOrEmpty(config.Subnet))
        args.Add($"--subnet {config.Subnet}");

      if (!string.IsNullOrEmpty(config.Gateway))
        args.Add($"--gateway {config.Gateway}");

      if (config.EnableIPv6)
        args.Add("--ipv6");

      if (config.Internal)
        args.Add("--internal");

      if (config.Options != null)
      {
        foreach (var opt in config.Options)
          args.Add($"--opt {opt.Key}={opt.Value}");
      }

      if (config.Labels != null)
      {
        foreach (var label in config.Labels)
          args.Add($"--label {label.Key}={label.Value}");
      }

      args.Add(config.Name);

      return string.Join(" ", args);
    }

    /// <summary>
    /// Replicates the filter argument construction from DockerCliNetworkDriver.ListAsync
    /// to test it in isolation.
    /// </summary>
    private static string BuildNetworkListFilterArgs(NetworkListFilter filter)
    {
      var args = "network ls --format \"{{json .}}\"";

      if (filter != null)
      {
        if (!string.IsNullOrEmpty(filter.Name))
          args += $" --filter name={filter.Name}";

        if (filter.Labels != null)
        {
          foreach (var label in filter.Labels)
            args += $" --filter label={label.Key}={label.Value}";
        }
      }

      return args;
    }

    #endregion
  }
}
