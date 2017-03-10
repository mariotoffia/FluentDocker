using System.Diagnostics;
using Ductus.FluentDocker.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest.CommandTests
{
  [TestClass]
  public class DockerInfoCommandTests : FluentDockerTestBase
  {
    [TestMethod]
    public void GetServerClientVersionInfoShallSucceed()
    {
      var result = Host.Host.Version(Host.Certificates);
      Assert.IsTrue(result.Success);
      Debug.WriteLine(result.Data.ToString());
    }
  }
}
