using System.IO;
using System.Threading.Tasks;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Services.Impl;
using Ductus.FluentDocker.Tests.Extensions;
using Ductus.FluentDockerTest;
using Ductus.FluentDockerTest.Compose;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class ComposeCommandTests : FluentDockerTestBase
  {
    [TestMethod]
    public async Task ComposeByBuildImageAddNgixAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster()
    {
      // Extract compose file and it's dependencies to a temp folder
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      var file = Path.Combine(fullPath, "docker-compose.yml");
      typeof(NsResolver).ResourceExtract(fullPath);

      try
      {
        var result = Host.Host.ComposeUp(composeFile: file, certificates: Host.Certificates);
        Assert.IsTrue(result.Success);

        var ids = Host.Host.ComposePs(composeFile: file, certificates: Host.Certificates);
        Assert.IsTrue(ids.Success);
        Assert.AreEqual(4, ids.Data.Count);

        // Find the nginx docker container
        DockerContainerService svc = null;
        foreach (var id in ids.Data)
        {
          var inspect = Host.Host.InspectContainer(id, Host.Certificates);
          Assert.IsTrue(inspect.Success);

          if (inspect.Data.Name.Contains("_nginx_"))
          {
            svc = new DockerContainerService(inspect.Data.Name.Substring(1), id, Host.Host,
              inspect.Data.State.ToServiceState(),
              Host.Certificates, false, false);
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
        Host.Host.ComposeDown(composeFile: file, certificates: Host.Certificates, removeVolumes: true,
          removeOrphanContainers: true);
      }
    }
  }
}