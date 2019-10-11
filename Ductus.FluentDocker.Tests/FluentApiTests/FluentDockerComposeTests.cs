using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HttpExtensions = Ductus.FluentDocker.Tests.Extensions.HttpExtensions;

// ReSharper disable StringLiteralTypo

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class FluentDockerComposeTests : FluentDockerTestBase
  {
    [TestMethod]
    public async Task WordPressDockerComposeServiceShallShowInstallScreen()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (var svc = Fd
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php") 
                        .Build().Start())
        // @formatter:on
      {
        // We now have a running WordPress with a MySql database        
        var installPage = await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
        Assert.AreEqual(1, svc.Hosts.Count);
        Assert.AreEqual(2, svc.Containers.Count);
        Assert.AreEqual(2, svc.Images.Count);
        Assert.AreEqual(5, svc.Services.Count);
      }
    }

    [TestMethod]
    public void KeepContainersShallWorkForCompositeServices()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      ICompositeService svc = null;
      IContainerService c1 = null;
      IContainerService c2 = null;
      try
      {
        svc = Fd
          .UseContainer()
          .UseCompose()
          .FromFile(file)
          .RemoveOrphans()
          .KeepContainer()
          .Build().Start();

        c1 = svc.Containers.First();
        c2 = svc.Containers.Skip(1).First();
        
        svc.Dispose();
        
        Assert.AreEqual(ServiceRunningState.Stopped, c1.State);
        Assert.AreEqual(ServiceRunningState.Stopped, c2.State);
      }
      finally
      {
        svc?.Dispose();
        
        c1?.Remove(true);
        c2?.Remove(true);
      }
    }

    [TestMethod]
    public void KeepRunningsShallWorkForCompositeServices()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      ICompositeService svc = null;
      IContainerService c1 = null;
      IContainerService c2 = null;
      try
      {
        svc = Fd
          .UseContainer()
          .UseCompose()
          .FromFile(file)
          .RemoveOrphans()
          .KeepRunning()
          .Build().Start();

        c1 = svc.Containers.First();
        c2 = svc.Containers.Skip(1).First();
        
        svc.Dispose();
        
        Assert.AreEqual(ServiceRunningState.Running, c1.State);
        Assert.AreEqual(ServiceRunningState.Running, c2.State);
      }
      finally
      {
        svc?.Dispose();
        
        c1?.Remove(true);
        c2?.Remove(true);
      }
    }

    [TestMethod]
    public async Task DockerComposePauseResumeShallWork()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (var svc = Fd
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php") 
                        .Build().Start())
        // @formatter:on
      {
        // We now have a running WordPress with a MySql database        
        var installPage = await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);

        svc.Pause();
        Assert.AreEqual(ServiceRunningState.Paused, svc.State);

        try
        {
          await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");
          Assert.Fail("The containers should be paused and thus no http get shall work");
        }
        catch (Exception)
        {
          // We shall end up here
        }

        foreach (var container in svc.Containers)
        {
          Assert.AreEqual(ServiceRunningState.Paused, container.State);
          var cfg = container.GetConfiguration(true);
          Assert.AreEqual(ServiceRunningState.Paused, cfg.State.ToServiceState());
        }

        svc.Start();
        Assert.AreEqual(ServiceRunningState.Running, svc.State);
        installPage = await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);

        foreach (var container in svc.Containers)
        {
          Assert.AreEqual(ServiceRunningState.Running, container.State);
          var cfg = container.GetConfiguration(true);
          Assert.AreEqual(ServiceRunningState.Running, cfg.State.ToServiceState());
        }
      }
    }

    [TestMethod]
    public async Task ComposeWaitForHttpShallWork()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (Fd
                .UseContainer()
                .UseCompose()
                .FromFile(file)
                .RemoveOrphans()
                .WaitForHttp("wordpress",  "http://localhost:8000/wp-admin/install.php", continuation: (resp, cnt) =>  
                             resp.Body.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1 ? 0 : 500)
                .Build().Start())
        // @formatter:on
      {
        // Since we have waited - this shall now always work.       
        var installPage = await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
    }

    [TestMethod]
    [ExpectedException(typeof(FluentDockerException))]
    public void ComposeWaitForHttpThatFailShallBeAborted()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      try
      {
        // @formatter:off
        using (Fd
                          .UseContainer()
                          .UseCompose()
                          .FromFile(file)
                          .RemoveOrphans()
                          .WaitForHttp("wordpress",
                                      "http://localhost:8000/wp-admin/install.php", 
                                      continuation: (resp, cnt) =>
                                      {
                                        if (cnt > 3) throw new FluentDockerException($"No Contact after {cnt} times");
                                        return resp.Body.IndexOf("ALIBABA", StringComparison.Ordinal) != -1 ? 0 : 500;
                                      })
                          .Build().Start())
          // @formatter:on

        {
          Assert.Fail("It should have thrown a FluentDockerException!");
        }
      }
      catch
      {
        // Manually remove containers since they are not cleaned up due to the error...
        foreach (var container in Fd.Native().GetContainers())
          if (container.Name.StartsWith("wordpress"))
            container.Dispose();

        throw;
      }
    }

    [TestMethod]
    public async Task ComposeWaitForCustomLambdaShallWork()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (Fd
                .UseContainer()
                .UseCompose()
                .FromFile(file)
                .RemoveOrphans()
                .Wait("wordpress", (service, cnt) => {
                    if (cnt > 60) throw new FluentDockerException("Failed to wait for wordpress service");
            
                    var res = "http://localhost:8000/wp-admin/install.php".DoRequest().Result;            
                    return res.Code == HttpStatusCode.OK && 
                           res.Body.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1 ? 0 : 500;
                  })
                .Build().Start())
        // @formatter:on
      {
        // Since we have waited - this shall now always work.       
        var installPage = await HttpExtensions.Wget("http://localhost:8000/wp-admin/install.php");
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
    }

    [TestMethod]
    public void ComposeRunOnRemoteMachineShallWork()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      using (
        var svc =
          new Builder().UseHost()
            .UseSsh("192.168.1.27").WithName("remote-daemon")
            .WithSshUser("solo").WithSshKeyPath("${E_LOCALAPPDATA}/lxss/home/martoffi/.ssh/id_rsa")
            .UseContainer()
            .UseCompose()
            .FromFile(file)
            .RemoveOrphans()
            .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php",
              continuation: (resp, cnt) =>
                resp.Body.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1 ? 0 : 500)
            .Build().Start())
      {
        Assert.AreEqual(1, svc.Hosts.Count);
        Assert.AreEqual(2, svc.Containers.Count);
        Assert.AreEqual(2, svc.Images.Count);
        Assert.AreEqual(5, svc.Services.Count);
      }
    }

    [TestMethod]
    public void Issue85()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/MongoDbAndNetwork/docker-compose.yml");

      using (var svc = Fd.UseContainer()
        .UseCompose()
        .FromFile(file)
        .Build()
        .Start())
      {
        var c = (IContainerService) svc.Services.Single(s => s is IContainerService);
        var nw = c.GetNetworks().Single();
        var ncfg = nw.GetConfiguration(true);

        Assert.AreEqual("mongodbandnetwork_mongodb-network", nw.Name);
        Assert.AreEqual(ncfg.Id, nw.Id);
      }
    }

    [TestMethod]
    public void Issue94()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/KafkaAndZookeeper/docker-compose.yaml");

      using (var svc = Fd.UseContainer()
        .UseCompose()
        .FromFile(file)
        .Build()
        .Start())
      {
        var kafka = svc.Services.OfType<IContainerService>().Single(x => x.Name == "kafka");
        var zookeeper = svc.Services.OfType<IContainerService>().Single(x => x.Name == "zookeeper");
        Assert.AreEqual("kafkaandzookeeper", kafka.Service);
        Assert.AreEqual("kafkaandzookeeper", zookeeper.Service);
        Assert.AreEqual("1", kafka.InstanceId);
        Assert.AreEqual("1", zookeeper.InstanceId);
      }
    }
  }
}