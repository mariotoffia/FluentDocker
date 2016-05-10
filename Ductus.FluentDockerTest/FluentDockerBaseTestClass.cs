using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  public abstract class FluentDockerBaseTestClass
  {
    protected IHostService Host;
    private bool _createdHost;

    [TestInitialize]
    public void Initialize()
    {
      EnsureDockerHost();
    }

    [TestCleanup]
    public void Teardown()
    {
      if (_createdHost)
      {
        "test-machine".Delete(true /*force*/);
      }
    }

    private void EnsureDockerHost()
    {
      if (Host?.State == ServiceRunningState.Running)
      {
        return;
      }

      var hosts = new Hosts().Discover();
      Host = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");

      if (null != Host)
      {
        if (Host.State != ServiceRunningState.Running)
        {
          Host.Start();
        }

        return;
      }

      if (hosts.Count > 0)
      {
        Host = hosts.First();
      }

      if (null != Host)
      {
        return;
      }

      if (_createdHost)
      {
        throw new Exception("Failed to initialize the test class, tried to create a docker host but failed");
      }

      var res = "test-machine".Create(1024, 20000, 1);
      Assert.AreEqual(true, res.Success);

      var start = "test-machine".Start();
      Assert.AreEqual(true, start.Success);

      _createdHost = true;
      EnsureDockerHost();
    }
  }
}