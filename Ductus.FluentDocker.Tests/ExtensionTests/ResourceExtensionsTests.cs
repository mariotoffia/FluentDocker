using System.Linq;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Tests.Compose;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ExtensionTests
{
#if !COREFX
  [TestClass]
  public class ResourceExtensionsTests
  {
    [TestMethod]
    public void QueryResourcesRecusivelyShallWork()
    {
      var resources = typeof(NsResolver);

      var res =
        resources.ResuorceQuery().Where(x => x.Resource.Equals("Dockerfile") || x.Resource.Equals("index.js")).ToArray();

      Assert.AreEqual(4, res.Length);
      Assert.AreEqual(3, res.Count(x => x.Resource == "Dockerfile"));
      Assert.AreEqual(1, res.Count(x => x.Resource == "index.js"));
    }

    [TestMethod]
    public void QueryResourcesNonRecurivelyShallWork()
    {
      var resources = typeof(NsResolver);

      var res = resources.ResuorceQuery(false).ToArray();

      Assert.AreEqual(1, res.Length);
      Assert.AreEqual("docker-compose.yml", res[0].Resource);
    }
  }
#endif
}
