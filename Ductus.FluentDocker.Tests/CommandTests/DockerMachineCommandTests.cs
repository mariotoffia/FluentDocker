using System.Linq;
using System.Net;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class DockerMachineCommandTests
  {
    [Ignore]
    [TestMethod]
    public void ManuallyForceDeleteMachine()
    {
      var res = "remote-docker-daemon-entry".ManuallyDelete();
      Assert.IsTrue(res.Success);
    }
    
    [TestMethod]
    [Ignore]
    public void InspectDockerMachine()
    {
      var res = "test-machine".Create(1024, 20000, 1);
      Assert.AreEqual(true, res.Success);
      Assert.AreEqual(string.Empty, res.Error);

      var start = "test-machine".Start();
      Assert.AreEqual(true, start.Success);
      var inspect = "test-machine".Inspect().Data;
      Assert.AreEqual(1024, inspect.MemorySizeMb);
      Assert.AreEqual(20000, inspect.StorageSizeMb);
      Assert.AreEqual("test-machine", inspect.Name);
      Assert.AreEqual(true, inspect.RequireTls);
      Assert.AreEqual(CommandDefaults.MachineDriver, inspect.DriverName);
      Assert.AreEqual(1, inspect.CpuCount);
      Assert.IsFalse(Equals(IPAddress.None, inspect.IpAddress));
      Assert.IsNotNull(inspect.AuthConfig);
      Assert.IsNotNull(inspect.AuthConfig.CaCertPath);
      Assert.IsNotNull(inspect.AuthConfig.CertDir);
      Assert.IsNotNull(inspect.AuthConfig.ClientCertPath);
      Assert.IsNotNull(inspect.AuthConfig.ClientKeyPath);
    }

    [TestMethod]
    [Ignore]
    public void CreateDockerMachineShallSucceed()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    [TestMethod]
    [Ignore]
    public void MachineLsWhenRunningContainerShallReturnStateAndValidUrl()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var ls = Machine.Ls();
        Assert.IsTrue(ls.Success);

        var machines = ls.Data;
        Assert.IsTrue(machines.Count > 0);

        var testMachine = ls.Data.First(x => x.Name == "test-machine");
        Assert.AreEqual(ServiceRunningState.Running, testMachine.State);
        Assert.IsNotNull(testMachine.Docker);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }


    [TestMethod]
    [Ignore]
    public void CreateAndStartStopDockerMachineShallSucceed()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        var stop = "test-machine".Stop();
        Assert.AreEqual(true, stop.Success);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    [TestMethod]
    [Ignore]
    public void CreateAndStartStopDockerMachineShallGiveSaneEnvironment()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        var env = "test-machine".Environment().Data;
        Assert.IsTrue(env.ContainsKey("DOCKER_HOST"));
        Assert.IsTrue(env.ContainsKey("DOCKER_TLS_VERIFY"));
        Assert.IsTrue(env.ContainsKey("DOCKER_CERT_PATH"));
        Assert.IsTrue(env.ContainsKey("DOCKER_MACHINE_NAME"));
        Assert.AreEqual("test-machine", env["DOCKER_MACHINE_NAME"]);

        var stop = "test-machine".Stop();
        Assert.AreEqual(true, stop.Success);
      }
      finally
      {
        "test-machine".Delete(true /*force*/);
      }
    }
  }
}
