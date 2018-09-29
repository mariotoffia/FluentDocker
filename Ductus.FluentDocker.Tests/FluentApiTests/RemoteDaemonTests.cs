using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class RemoteDaemonTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestMethod]
    public void CreateSshConnectionToRemoteDockerAndCreateContainerShallWork()
    {
      using (
        var container =
          new Builder().UseHost()
            .UseSsh("192.168.1.27").WithName("remote-daemon")
            .WithSshUser("solo").WithSshKeyPath("${E_LOCALAPPDATA}/lxss/home/martoffi/.ssh/id_rsa").Host()
            .UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }
    
    [TestMethod]
    public void UseNamedDockerMachineForRemoteSshDaemonConnectionShallWork()
    {
      var remoteHost = new Builder().UseHost()
        .UseSsh("192.168.1.27").WithName("remote-daemon")
        .WithSshUser("solo").WithSshKeyPath("${E_LOCALAPPDATA}/lxss/home/martoffi/.ssh/id_rsa").Build();
      
      Assert.IsTrue(remoteHost.Host.ToString().StartsWith("tcp://"));
      
      using (
        var container =
          new Builder().UseHost().UseMachine().WithName("remote-daemon")
            .UseContainer()
            .UseImage("postgres:9.6-alpine")
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .Build())
      {
        Assert.AreEqual(ServiceRunningState.Stopped, container.State);
      }
    }

  }
}