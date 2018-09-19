using System;
using System.IO;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
      using (var svc = new Builder()
                        .UseContainer()
                        .UseCompose()
                        .FromFile(file)
                        .RemoveOrphans()
                        .Build().Start())
        // @formatter:on
      {
        // We now have a running WordPress with a MySql database        
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();

        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
        Assert.AreEqual(1, svc.Hosts.Count);
        Assert.AreEqual(2, svc.Containers.Count);
        Assert.AreEqual(2, svc.Images.Count);
        Assert.AreEqual(5, svc.Services.Count);
      }
    }
  }
}