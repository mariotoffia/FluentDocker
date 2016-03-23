using Ductus.FluentDocker.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class PostgresMsTests : PostgresTestBase
  {
    [TestMethod]
    public void RunningContainerAndConnectionStringSetWithinTestMethod()
    {
      Assert.IsNotNull(Container);
      Assert.AreEqual(string.Format(PostgresConnectionString, Container.Host,
        Container.GetHostPort("5432/tcp"), PostgresUser,
        PostgresPassword, PostgresDb), ConnectionString);
    }
  }
}