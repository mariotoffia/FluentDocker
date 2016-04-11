using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class DockerClientCommandTests
  {
    private static string _caCertPath;
    private static string _clientCertPath;
    private static string _clientKeyPath;
    private static Uri _docker;
    private static bool _createdTestMachine;

    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
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
      _caCertPath = inspect.AuthConfig.CaCertPath;
      _clientCertPath = inspect.AuthConfig.ClientCertPath;
      _clientKeyPath = inspect.AuthConfig.ClientKeyPath;
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
    public void RunWithoutArgumentShallSucceed()
    {
      string id = null;
      try
      {
        var cmd =_docker.Run("nginx:latest", null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(cmd.Success);

        id = cmd.Data;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(id));
        Assert.AreEqual(64, id.Length);
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        }
      }
    }

    [TestMethod]
    public void RemoveContainerShallSucceed()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:latest", null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(cmd.Success);

        id = cmd.Data;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(id));
        Assert.AreEqual(64, id.Length);

        var rm = _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(rm.Success);
        Assert.AreEqual(id, rm.Data);
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        }
      }

    }


    [TestMethod]
    public void DockerPsWithOneContainerShallGiveOneResult()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:latest", null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(cmd.Success);

        id = cmd.Data;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(id));
        Assert.AreEqual(64, id.Length);

        var ls = _docker.Ps(null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(ls.Success);
        Assert.AreEqual(1,ls.Data.Count);
        Assert.IsTrue(id.StartsWith(ls.Data[0]));
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        }
      }
    }

    [TestMethod]
    public void InspectRunningContainerShallSucceed()
    {
      string id = null;
      try
      {
        var cmd = _docker.Run("nginx:latest", null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(cmd.Success);

        id = cmd.Data;
        Assert.IsTrue(!string.IsNullOrWhiteSpace(id));
        Assert.AreEqual(64, id.Length);

        var inspect = _docker.InspectContainer(id, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(inspect.Success);
        Assert.IsTrue(inspect.Data.Name.Length > 2);
        Assert.AreEqual(true, inspect.Data.State.Running);
      }
      finally
      {
        if (null != id)
        {
          _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        }
      }
    }
  }
}