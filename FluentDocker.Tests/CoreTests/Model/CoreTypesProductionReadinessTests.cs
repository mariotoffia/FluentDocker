using System;
using System.Collections;
using System.Reflection;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class CoreTypesProductionReadinessTests
  {
    [Fact]
    public void ContainerBuildParams_ToString_SeparatesCpusetAndIsolationOptions()
    {
#pragma warning disable CS0618
      var value = new ContainerBuildParams
      {
        AllowCpuExecution = "0-3",
        ForceRemoveIntermediateContainers = true,
        Isolation = ContainerIsolationTechnology.Process
      }.ToString();
#pragma warning restore CS0618

      Assert.Contains("--cpuset-cpus 0-3", value);
      Assert.Contains(" --force-rm --isolation process", value);
      Assert.DoesNotContain("--cpuset-cpus0-3", value);
      Assert.DoesNotContain("--force-rm--isolation", value);
    }

    [Fact]
    public void ContainerInspect_WithSecondaryNetworkAddresses_Deserializes()
    {
      const string json = """
          {
            "Id": "abc",
            "NetworkSettings": {
              "SecondaryIPAddresses": [{"Addr": "172.18.0.3", "PrefixLen": 16}],
              "SecondaryIPv6Addresses": [{"Addr": "fd00::2", "PrefixLen": 64}]
            }
          }
          """;

      var container = JsonHelper.TryDeserialize<Container>(json);

      Assert.NotNull(container);
      Assert.NotNull(container.NetworkSettings);
      AssertSecondaryAddress(container.NetworkSettings.SecondaryIPAddresses, "172.18.0.3", 16);
      AssertSecondaryAddress(container.NetworkSettings.SecondaryIPv6Addresses, "fd00::2", 64);
    }

    [Fact]
    public void DriverEnums_DefaultToUnknown()
    {
      Assert.Equal(RuntimeType.Unknown, default);
      Assert.Equal(DriverType.Unknown, default);
    }

    private static void AssertSecondaryAddress(object value, string addr, int prefixLen)
    {
      var list = Assert.IsAssignableFrom<IEnumerable>(value);
      var enumerator = list.GetEnumerator();
      Assert.True(enumerator.MoveNext());
      var item = enumerator.Current;
      Assert.NotNull(item);
      Assert.Equal(addr, GetPropertyValue<string>(item, "Addr"));
      Assert.Equal(prefixLen, GetPropertyValue<int>(item, "PrefixLen"));
      Assert.False(enumerator.MoveNext());
    }

    private static T GetPropertyValue<T>(object value, string name)
    {
      var property = value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
      Assert.NotNull(property);
      return Assert.IsType<T>(property.GetValue(value));
    }
  }
}
