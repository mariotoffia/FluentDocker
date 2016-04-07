using System.Net;
using Ductus.FluentDocker.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class DockerMachineCommandTests
  {
    [TestMethod]
    public void InspectDockerMachine()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var inspect = "test-machine".Inspect();
        Assert.AreEqual(1024,inspect.MemorySizeMb);
        Assert.AreEqual(20000,inspect.StorageSizeMb);
        Assert.AreEqual("test-machine",inspect.Name);
        Assert.AreEqual(true, inspect.RequireTls);
        Assert.AreEqual("virtualbox",inspect.DriverName);
        Assert.AreEqual(1,inspect.CpuCount);
        Assert.IsFalse(Equals(IPAddress.None, inspect.IpAddress));
        Assert.IsNotNull(inspect.AuthConfig);
        Assert.IsNotNull(inspect.AuthConfig.CaCertPath);
        Assert.IsNotNull(inspect.AuthConfig.CertDir);
        Assert.IsNotNull(inspect.AuthConfig.ClientCertPath);
        Assert.IsNotNull(inspect.AuthConfig.ClientKeyPath);
      }
      finally
      {
        "test-machine".Delete(true/*force*/);
      }
    }

    [TestMethod]
    public void CreateDockerMachineShallSucceed()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);
      }
      finally
      {
        "test-machine".Delete(true/*force*/);
      }
    }

    [TestMethod]
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
        "test-machine".Delete(true/*force*/);
      }
    }

    [TestMethod]
    public void CreateAndStartStopDockerMachineShallGiveSaneEnvironment()
    {
      try
      {
        var res = "test-machine".Create(1024, 20000, 1);
        Assert.AreEqual(true, res.Success);

        var start = "test-machine".Start();
        Assert.AreEqual(true, start.Success);

        var env = "test-machine".Environment();
        Assert.IsTrue(env.ContainsKey("DOCKER_HOST"));
        Assert.IsTrue(env.ContainsKey("DOCKER_TLS_VERIFY"));
        Assert.IsTrue(env.ContainsKey("DOCKER_CERT_PATH"));
        Assert.IsTrue(env.ContainsKey("DOCKER_MACHINE_NAME"));
        Assert.AreEqual("test-machine",env["DOCKER_MACHINE_NAME"]);

        var stop = "test-machine".Stop();
        Assert.AreEqual(true, stop.Success);
      }
      finally
      {
        "test-machine".Delete(true/*force*/);
      }
    }
  }
}