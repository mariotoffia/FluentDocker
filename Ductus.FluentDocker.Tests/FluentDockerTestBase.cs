using System;
using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests
{
  public abstract class FluentDockerTestBase
  {
    protected IHostService DockerHost;
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
      if (DockerHost?.State == ServiceRunningState.Running)
      {
        return;
      }

      var hosts = new Hosts().Discover();
      DockerHost = hosts.FirstOrDefault(x => x.IsNative) ?? hosts.FirstOrDefault(x => x.Name == "default");

      if (null != DockerHost)
      {
        if (DockerHost.State != ServiceRunningState.Running)
        {
          DockerHost.Start();
          DockerHost.Host.LinuxMode(DockerHost.Certificates);
        }

        return;
      }

      if (hosts.Count > 0)
      {
        DockerHost = hosts.First();
      }

      if (null != DockerHost)
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
