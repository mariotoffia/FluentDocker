using System.Diagnostics;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDockerTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
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
