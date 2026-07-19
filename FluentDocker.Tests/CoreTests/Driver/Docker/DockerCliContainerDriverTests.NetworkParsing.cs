using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentDocker.Common;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Regression tests for issue #335: <c>docker inspect</c> emits
  /// <c>NetworkSettings.LinkLocalIPv6PrefixLen</c>, <c>GlobalIPv6PrefixLen</c> and
  /// <c>IPPrefixLen</c> as JSON <em>numbers</em>, while some engines (and Podman) emit
  /// numeric <em>strings</em>. The properties are lenient <see cref="int"/>s
  /// (<see cref="LenientInt32Converter"/>), so both shapes parse and drift degrades to 0
  /// instead of failing the inspect.
  /// </summary>
  public partial class DockerCliContainerDriverTests
  {
    #region Network Settings Numeric Prefix Length Parsing (issue #335)

    [Fact]
    public void InspectParsing_NumericNetworkPrefixLengths_DoesNotThrow()
    {
      // Mirrors the exact shape `docker inspect` returns: prefix-len fields are numbers.
      var json = @"[{
        ""Id"": ""abc123"",
        ""Name"": ""/ipv6-container"",
        ""NetworkSettings"": {
          ""IPAddress"": ""172.17.0.2"",
          ""IPPrefixLen"": 16,
          ""GlobalIPv6Address"": ""2001:db8::2"",
          ""GlobalIPv6PrefixLen"": 64,
          ""LinkLocalIPv6Address"": ""fe80::42:acff:fe11:2"",
          ""LinkLocalIPv6PrefixLen"": 0
        }
      }]";

      var ex = Record.Exception(() => JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions));

      Assert.Null(ex);
    }

    [Fact]
    public void InspectParsing_NumericNetworkPrefixLengths_PreservesValues()
    {
      var json = @"[{
        ""Id"": ""abc123"",
        ""Name"": ""/ipv6-container"",
        ""NetworkSettings"": {
          ""IPPrefixLen"": 16,
          ""GlobalIPv6PrefixLen"": 64,
          ""LinkLocalIPv6PrefixLen"": 0
        }
      }]";

      var ns = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions)?.FirstOrDefault()?.NetworkSettings;

      Assert.NotNull(ns);
      Assert.Equal(16, ns.IPPrefixLen);
      Assert.Equal(64, ns.GlobalIPv6PrefixLen);
      Assert.Equal(0, ns.LinkLocalIPv6PrefixLen);
    }

    [Fact]
    public void InspectParsing_StringNetworkPrefixLengths_StillParse()
    {
      // Some engines (and Podman) emit these as strings; that must keep working.
      var json = @"[{
        ""Id"": ""abc123"",
        ""NetworkSettings"": { ""IPPrefixLen"": ""24"" }
      }]";

      var ns = JsonSerializer.Deserialize<List<Container>>(
          json, JsonHelper.CaseInsensitiveOptions)?.FirstOrDefault()?.NetworkSettings;

      Assert.NotNull(ns);
      Assert.Equal(24, ns.IPPrefixLen);
    }

    #endregion
  }
}
