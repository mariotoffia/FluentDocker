using System;
using Ductus.FluentDocker.Commands;
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
    private static TestContext _ctx;
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      _ctx = ctx;
      _ctx.WriteLine("Creating test-machine for all class tests");
      var res = "test-machine".Create(1024, 20000, 1);
      Assert.AreEqual(true, res.Success);

      var inspect = "test-machine".Inspect();

      _docker = "test-machine".Uri();
      _caCertPath = inspect.AuthConfig.CaCertPath;
      _clientCertPath = inspect.AuthConfig.ClientCertPath;
      _clientKeyPath = inspect.AuthConfig.ClientKeyPath;
    }

    [ClassCleanup]
    public static void TearDown()
    {
      _ctx.WriteLine("Delete test-machine used by tests");
      "test-machine".Delete(true /*force*/);
    }

    [TestMethod]
    public void RunWithoutArgumentShallSucceed()
    {
      string id = null;
      try
      {
        id = _docker.Run("nginx:latest", null, _caCertPath, _clientCertPath, _clientKeyPath);
        Assert.IsTrue(!string.IsNullOrWhiteSpace(id));
      }
      finally
      {
        if (null != id)
        {
          _docker.Stop(id, null, _caCertPath, _clientCertPath, _clientKeyPath);
          _docker.RemoveContainer(id, true, true, null, _caCertPath, _clientCertPath, _clientKeyPath);
        }
      }
    }
  }
}