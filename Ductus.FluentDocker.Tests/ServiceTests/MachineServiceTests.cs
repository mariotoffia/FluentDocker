using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public class MachineServiceTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestCategory("Main")]
    [TestMethod]
    public void DiscoverBinariesShallWork()
    {
      var resolver = new DockerBinariesResolver();
      
      Console.WriteLine(
        $"{resolver.MainDockerClient.FqPath} {resolver.MainDockerMachine.FqPath} {resolver.MainDockerCompose.FqPath}");
    }
    [TestCategory("Main")]
    [TestMethod]
    public void DiscoverShouldReturnNativeWhenSuchIsPresent()
    {
      if (!(CommandExtensions.IsEmulatedNative() || CommandExtensions.IsNative()))
      {
        return;
      }

      var services = new Hosts().Discover();
      Assert.IsTrue(services.Count > 0);

      var native = services.First(x => x.IsNative);
      Assert.AreEqual("native",native.Name);
      Assert.AreEqual(true,native.IsNative);
    }

    [TestCategory("Machine-Only")]
    [TestMethod]
    [Ignore]
    public void DiscoverShallReturnMachines()
    {
      if (!CommandExtensions.IsToolbox())
      {
        return;
      }

      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        var hosts = new Hosts().Discover();
        Assert.IsTrue(hosts.Count > 0);

        var tm = hosts.First(x => x.Name == "test-machine");
        Assert.IsNotNull(tm);
        Assert.AreEqual(false, tm.IsNative);
        Assert.AreEqual(ServiceRunningState.Running, tm.State);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }
  }
}
