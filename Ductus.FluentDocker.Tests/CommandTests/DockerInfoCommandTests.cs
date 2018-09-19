using System.Diagnostics;
using Ductus.FluentDocker.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public class DockerInfoCommandTests : FluentDockerTestBase
  {
    [TestMethod]
    public void GetServerClientVersionInfoShallSucceed()
    {
      var result = DockerHost.Host.Version(DockerHost.Certificates);
      Assert.IsTrue(result.Success);
      Debug.WriteLine(result.Data.ToString());
    }
  }
}
