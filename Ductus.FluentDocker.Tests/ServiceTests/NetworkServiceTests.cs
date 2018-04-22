using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public sealed class NetworkServiceTests
  {
    private static IHostService _host;
    private static bool _createdHost;

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      var hosts = new Hosts().Discover();
      _host = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");

      if (null != _host && _host.State != ServiceRunningState.Running)
      {
        _host.Start();
        _host.Host.LinuxMode(_host.Certificates);
        return;
      }

      if (null == _host && hosts.Count > 0)
        _host = hosts.First();

      if (null == _host)
      {
        if (_createdHost)
          throw new Exception("Failed to initialize the test class, tried to create a docker host but failed");

        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        _createdHost = true;
        Initialize(ctx);
      }
    }

    [ClassCleanup]
    public static void TearDown()
    {
      if (_createdHost)
        "test-machine".Delete(true /*force*/);
    }

    [TestMethod]
    public void DiscoverNetworksShallWork()
    {
      var networks = _host.GetNetworks();
      Assert.IsTrue(networks.Count > 0);
      Assert.IsTrue(networks.Count(x => x.Name == "bridge") == 1);
      Assert.IsTrue(networks.Count(x => x.Name == "host") == 1);
    }

    [TestMethod]
    public void NetworkIsDeletedWhenDisposedAndFlagIsSet()
    {
      using (var nw = _host.CreateNetwork("unit-test-network", removeOnDispose: true))
      {
        Assert.IsTrue(nw.Id.Length > 0);
        Assert.AreEqual("unit-test-network", nw.Name);
      }

      var networks = _host.GetNetworks();
      Assert.IsTrue(networks.Count(x => x.Name == "unit-test-network") == 0);
    }
  }
}