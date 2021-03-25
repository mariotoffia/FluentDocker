using System;
using System.Net;
using System.Threading.Tasks;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class FluentNetworkTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestMethod]
    public void StaticIpv4InCustomNetworkShallWork()
    {
      using (var nw = Fd.UseNetwork("unit-test-nw")
        .UseSubnet("10.18.0.0/16").Build())
      {
        using (
          var container =
            Fd.UseContainer()
              .WithName("mycontainer")
              .UseImage("postgres:9.6-alpine")
              .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
              .ExposePort(5432)
              .UseNetwork(nw)
              .UseIpV4("10.18.0.22")
              .WaitForPort("5432/tcp", 30000 /*30s*/)
              .Build()
              .Start())
        {
          var ip = container.GetConfiguration().NetworkSettings.Networks["unit-test-nw"].IPAddress;
          Assert.AreEqual("10.18.0.22", ip);
        }
      }
    }

    [TestMethod]
    public void InternalNetworkExposedToHostShallWork()
    {
      using (var nw = Fd.UseNetwork("unit-test-nw")
                        .UseDriver("bridge")
                        .IsInternal()
                        .Build())
      {
        // Ping outside shall not work
        using (
          var container =
            Fd.UseContainer()
              .WithName("internal-container")
              .UseImage("alpine")
              .UseNetwork(nw)
              .Command("ping", "1.1.1.1", "-c 1", "-w 5", "-W 5")
              .Build()
              .Start())
        {
          var log = container.Logs().ReadToEnd(30_000);
          var c = container.GetConfiguration(true);
          var ec = c.State.ExitCode;
          Assert.AreEqual(1, ec);
        }

        // Gain access to the container from host shall work
        using (var container =
          Fd.UseContainer()
            .UseImage("nginx:1.13.6-alpine")
            .ExposePort(80)
            .UseNetwork(nw)
            .WaitForPort("80/tcp", 30000 /*30s*/)
            .Build()
            .Start())
        {
          var response = $"http://{container.ToHostExposedEndpoint("80/tcp")}".Wget().Result;
          Console.WriteLine(response);
        }
      }
    }

    [TestMethod]
    public void CustomResolverForContainerShallWork()
    {
      bool executedCustomResolver = false;

      using (
        var container =
          Fd.UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .ExposePort(5432)
            .UseCustomResolver((
              ports, portAndProto, dockerUri) =>
            {
              executedCustomResolver = true;
              
              if (null == ports || string.IsNullOrEmpty(portAndProto))
                return null;

              if (!ports.TryGetValue(portAndProto, out var endpoints))
                return null;

              if (null == endpoints || endpoints.Length == 0)
                return null;

              if (CommandExtensions.IsNative())
                return endpoints[0];

              if (CommandExtensions.IsEmulatedNative())
                return CommandExtensions.IsDockerDnsAvailable()
                  ? new IPEndPoint(CommandExtensions.EmulatedNativeAddress(), endpoints[0].Port)
                  : new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);

              if (Equals(endpoints[0].Address, IPAddress.Any) && null != dockerUri)
                return new IPEndPoint(IPAddress.Parse(dockerUri.Host), endpoints[0].Port);

              return endpoints[0];
            })
            .WaitForPort("5432/tcp", 30000 /*30s*/)
            .Build()
            .Start())
      {
        var state = container.GetConfiguration(true/*force*/).State.ToServiceState();
        Assert.AreEqual(ServiceRunningState.Running, state);
        Assert.IsTrue(executedCustomResolver);
      }
    }
  }
}
