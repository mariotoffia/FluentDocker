using System.Linq;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class FluentContainerBasicTests
  {
    [TestMethod]
    public void BuildContainerRenderServiceInStoppedMode()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

    [TestMethod]
    public void BuildAndStartContainerWithKeepContainerWillLeaveContainerInArchve()
    {
      string id;
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .KeepContainer()
            .Build())
      {
        id = container.Id;
      }

      Assert.IsNotNull(id);
      bool found = false;
      foreach (var host in new Hosts().Discover())
      {
        var container = host.GetContainers().FirstOrDefault(x => x.Id == id);
        if (null == container)
        {
          continue;
        }

        found = true;
        container.Remove(true);
        break;
      }

      Assert.IsTrue(found);
    }

    [TestMethod]
    public void BuildAndStartContainerWithCustomEnvironmentWillBeReflectedInGetConfiguration()
    {
      using (
        var container =
          new Builder().UseContainer()
            .UseImage("postgres:latest")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        container.Start();
        var config = container.GetConfiguration();

        Assert.AreEqual(ServiceRunningState.Running, container.State);
        Assert.IsTrue(config.Config.Env.Any(x => x == "POSTGRES_PASSWORD=mysecretpassword"));
      }
    }
  }
}