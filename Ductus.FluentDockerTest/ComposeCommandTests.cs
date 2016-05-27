using System.IO;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDockerTest.Compose;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ComposeCommandTests : FluentDockerTestBase
  {
    [TestMethod]
    public void ComposeByBuildImageAddNgixAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster()
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
        Assert.AreEqual(2, ids.Data.Count);

        foreach (var id in ids.Data)
        {
          var inspect = Host.Host.InspectContainer(id, Host.Certificates);
          Assert.IsTrue(inspect.Success);
        }

        // TODO: Test the cluster by firing a wget as in multicontainer tests
      }
      finally
      {
        Host.Host.ComposeDown(composeFile: file, certificates: Host.Certificates);
      }
    }
  }
}