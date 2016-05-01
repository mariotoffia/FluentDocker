using System;
using System.Linq;
using Ductus.FluentDocker;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class MachineServiceTests
  {
    [TestCategory("Main")]
    [TestMethod]
    public void DiscoverShouldReturnNativeWhenSuchIsPresent()
    {
      if (!(CommandExtensions.IsEmulatedNative() || CommandExtensions.IsNative()))
      {
        return;
      }

      var services = new Hosts().Discover();
      Assert.AreEqual(1,services.Count);

      var native = services.First();
      Assert.AreEqual("native",native.Name);
      Assert.AreEqual(true,native.IsNative);
    }

    [TestCategory("Machine-Only")]
    [TestMethod]
    public void DiscoverShallReturnMachines()
    {
      if (!CommandExtensions.IsMachine())
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
