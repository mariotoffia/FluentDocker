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
        Assert.IsNotNull(id);
      }

      // We shall have the container as stopped by now.
      var cont =
        new Hosts()
          .Discover()
          .Select(host => host.GetContainers().FirstOrDefault(x => x.Id == id))
          .FirstOrDefault(container => null != container);

      Assert.IsNotNull(cont);
      cont.Remove(true);
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