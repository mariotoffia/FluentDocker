using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

// ReSharper disable StringLiteralTypo

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public class MachineServiceTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      try
      {
        Utilities.LinuxMode();
      }
      catch
      {
        Console.WriteLine("Got exception while setting to linux mode, but this is ok in some situation...");
      }
    }

    [TestCategory("Main")]
    [TestMethod]
    public void DiscoverBinariesShallWork()
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null);

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
      IsTrue(services.Count > 0);

      var native = services.First(x => x.IsNative);
      AreEqual("native", native.Name);
      AreEqual(true, native.IsNative);
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
        AreEqual(true, res.Success);

        var start = "test-machine".Start();
        AreEqual(true, start.Success);

        var hosts = new Hosts().Discover();
        IsTrue(hosts.Count > 0);

        var tm = hosts.First(x => x.Name == "test-machine");
        IsNotNull(tm);
        AreEqual(false, tm.IsNative);
        AreEqual(ServiceRunningState.Running, tm.State);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    [TestCategory("Machine-Only")]
    [TestMethod]
    [ExpectedException(typeof(FluentDockerException))]
    public void CreateHostFromNonExistingMachineRegistryEntryShallThrowExceptionWhenThrowIfNotStarted()
    {
      new Hosts().FromMachineName("kalleKula_not_a_regentry", throwIfNotStarted: true);
    }

    [TestCategory("Machine-Only")]
    [TestMethod]
    [Ignore]
    public void CreateHostFromExistingMachineRegistryEntryShallWork()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        AreEqual(true, res.Success);

        var host = new Hosts().FromMachineName("test-machine");
        IsNotNull(host);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }
  }
}
