using System.IO;
using System.Threading.Tasks;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Services.Impl;
using Ductus.FluentDocker.Tests.Compose;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class ComposeCommandTests : FluentDockerTestBase
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestMethod]
    public async Task
      ComposeByBuildImageAddNginxAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster()
    {
      // Extract compose file and it's dependencies to a temp folder
      var fullPath = (TemplateString)@"${TEMP}\fluentdockertest\${RND}";
      var file = Path.Combine(fullPath, "docker-compose.yml");
      typeof(NsResolver).ResourceExtract(fullPath);

      try
      {
        var result = DockerHost.Host
        .ComposeUpCommand(new Commands.Compose.ComposeUpCommandArgs
        {
          ComposeFiles = new System.Collections.Generic.List<string>() { file },
          Certificates = DockerHost.Certificates
        });

        Assert.IsTrue(result.Success);

        var ids = DockerHost.Host.ComposePs(composeFile: file, certificates: DockerHost.Certificates);
        Assert.IsTrue(ids.Success);
        Assert.AreEqual(5, ids.Data.Count);

        // Find the nginx docker container
        DockerContainerService svc = null;
        foreach (var id in ids.Data)
        {
          var inspect = DockerHost.Host.InspectContainer(id, DockerHost.Certificates);
          Assert.IsTrue(inspect.Success);

          if (inspect.Data.Name.Contains("_nginx_"))
          {
            svc = new DockerContainerService(inspect.Data.Name.Substring(1), id, DockerHost.Host,
              inspect.Data.State.ToServiceState(),
              DockerHost.Certificates, false, false);
            break;
          }
        }

        Assert.IsNotNull(svc);

        var ep = svc.ToHostExposedEndpoint("80/tcp");
        Assert.IsNotNull(ep);

        var round1 = await $"http://{ep.Address}:{ep.Port}".Wget();
        Assert.AreEqual("This page has been viewed 1 times!", round1);

        var round2 = await $"http://{ep.Address}:{ep.Port}".Wget();
        Assert.AreEqual("This page has been viewed 2 times!", round2);
      }
      finally
      {
        DockerHost.Host.ComposeDown(composeFile: file, certificates: DockerHost.Certificates, removeVolumes: true,
          removeOrphanContainers: true);
      }
    }

    [TestMethod]
    //[Ignore]
    public void Issue79_DockerComposeOnDockerMachineShallWork()
    {
      var fullPath = (TemplateString)@"${TEMP}\fluentdockertest\${RND}";
      var file = Path.Combine(fullPath, "docker-compose.yml");
      typeof(NsResolver).ResourceExtract(fullPath);

      var hostService = new DockerHostService("wifi-test");

      var composeResponse = hostService.Host
        .ComposeUpCommand(new Commands.Compose.ComposeUpCommandArgs
        {
          ComposeFiles = new System.Collections.Generic.List<string>() { file },
          Certificates = hostService.Certificates
        });
    }

    [TestMethod]
    public void WaitFlagAndWaitTimeoutWorks()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString)"Resources/ComposeTests/RabbitMQ/docker-compose.yml");

      var hostService = new DockerHostService("test");

      var composeResponse = hostService.Host
        .ComposeUpCommand(new Commands.Compose.ComposeUpCommandArgs
        {
          ComposeFiles = new System.Collections.Generic.List<string> { file },
          Certificates = hostService.Certificates,
          Wait = true,
          WaitTimeoutSeconds = 100,
        });

      Assert.IsTrue(composeResponse.Success);
    }
  }
}
