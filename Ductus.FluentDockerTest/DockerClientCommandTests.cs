using System;
using Ductus.FluentDocker.Commands;
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
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      var status = "test-machine".Status();
      if (status != ServiceRunningState.Running)
      {
        "test-machine".Create(1024, 20000, 1);
      }

      var inspect = "test-machine".Inspect().Data;

      _docker = "test-machine".Uri();
      _caCertPath = inspect.AuthConfig.CaCertPath;
      _clientCertPath = inspect.AuthConfig.ClientCertPath;
      _clientKeyPath = inspect.AuthConfig.ClientKeyPath;
    }

    [ClassCleanup]
    public static void TearDown()
    {
      //"test-machine".Delete(true /*force*/);
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