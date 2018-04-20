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
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    [TestMethod]
    public void DiscoverNetworksShallWork()
    {
      var networks = _docker.NetworkLs(_certificates);
      Assert.IsTrue(networks.Success);
      Assert.IsTrue(networks.Data.Count > 0);
      Assert.IsTrue(networks.Data.Count(x => x.Name == "bridge") == 1);
      Assert.IsTrue(networks.Data.Count(x => x.Name == "host") == 1);
    }
  }
}