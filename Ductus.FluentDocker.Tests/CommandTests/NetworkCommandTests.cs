using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public sealed class NetworkCommandTests
  {
    private static CertificatePaths _certificates;
    private static DockerUri _docker;
    private static bool _createdTestMachine;

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      if (CommandExtensions.IsNative() || CommandExtensions.IsEmulatedNative())
      {
        _docker.LinuxMode();
        return;
      }

      var machineName = "test-machine";

      var machines = Machine.Ls();
      if (machines.Success && machines.Data.Any(x => x.Name == "default"))
      {
        machineName = "default";
      }
      else
      {
        machineName.Create(1024, 20000, 1);
        _createdTestMachine = true;
      }

      machineName.Start();
      var inspect = machineName.Inspect().Data;

      _docker = machineName.Uri();
      _certificates = new CertificatePaths
      {
        CaCertificate = inspect.AuthConfig.CaCertPath,
        ClientCertificate = inspect.AuthConfig.ClientCertPath,
        ClientKey = inspect.AuthConfig.ClientKeyPath
      };

      _docker.LinuxMode(_certificates);
    }

    [ClassCleanup]
    public static void TearDown()
    {
      if (_createdTestMachine)
        "test-machine".Delete(true /*force*/);
    }

    [TestMethod]
    public void NetworkDiscoverShallWork()
    {
      var networks = _docker.NetworkLs(_certificates);
      Assert.IsTrue(networks.Success);
      Assert.IsTrue(networks.Data.Count > 0);
      Assert.IsTrue(networks.Data.Count(x => x.Name == "bridge") == 1);
      Assert.IsTrue(networks.Data.Count(x => x.Name == "host") == 1);
    }

    [TestMethod]
    public void NetworkInspectShallWork()
    {
      var networks = _docker.NetworkLs(_certificates);
      var first = _docker.NetworkInspect(network: networks.Data[0].Id);
      Assert.IsTrue(first.Success);
      Assert.IsNotNull(first.Data);
      Assert.IsNotNull(first.Data.IPAM);
    }

    [TestMethod]
    public void NetworkCreateAndDeleteShallWork()
    {
      string id = null;
      try
      {
        var created = _docker.NetworkCreate("unit-test-nw");
        if (created.Success)
          id = created.Data[0];

        Assert.IsNotNull(id);
      }
      finally
      {
        if (null == id)
        {
          var networks = _docker.NetworkLs(_certificates);
          if (networks.Success)
            id = networks.Data.Where(x => x.Name == "unit-test-nw").Select(x => x.Id).FirstOrDefault();
        }

        if (null != id)
          _docker.NetworkRm(network: id);
      }
    }

    [TestMethod]
    public void ConnectAndDisconnectContainerToNetworkShallWork()
    {
      string id = null;
      string container = null;
      try
      {
        var cmd = _docker.Run("postgres:9.6-alpine", new ContainerCreateParams
        {
          PortMappings = new[] {"40001:5432"},
          Environment = new[] {"POSTGRES_PASSWORD=mysecretpassword"}
        }, _certificates);

        Assert.IsTrue(cmd.Success);
        container = cmd.Data;

        var created = _docker.NetworkCreate("unit-test-nw");
        if (created.Success)
          id = created.Data[0];
        Assert.IsNotNull(id);

        _docker.NetworkConnect(container, id);
        var inspect = _docker.NetworkInspect(network: id);
        Assert.IsTrue(inspect.Success);
        Assert.IsTrue(inspect.Data.Containers.ContainsKey(container));

        var disconnect = _docker.NetworkDisconnect(container, id, true /*force*/);
        Assert.IsTrue(disconnect.Success);

        inspect = _docker.NetworkInspect(network: id);
        Assert.IsFalse(inspect.Data.Containers.ContainsKey(container));
      }
      finally
      {
        if (null != container)
          _docker.RemoveContainer(container, true, true);

        if (null == id)
        {
          var networks = _docker.NetworkLs(_certificates);
          if (networks.Success)
            id = networks.Data.Where(x => x.Name == "unit-test-nw").Select(x => x.Id).FirstOrDefault();
        }

        if (null != id)
          _docker.NetworkRm(network: id);
      }
    }
  }
}