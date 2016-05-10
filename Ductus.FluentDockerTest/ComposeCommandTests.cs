using Ductus.FluentDockerTest.Compose;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ComposeCommandTests : FluentDockerBaseTestClass
  {
    [TestMethod]
    public void ComposeByBuildImageAddNgixAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster()
    {
      var composeFiles = typeof(NsResolver).Namespace;

      // TODO: Replicate test in multicontainer tests
    }
  }
}