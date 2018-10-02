using System;
using System.IO;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// ReSharper disable StringLiteralTypo

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
                        .WaitForHttp("wordpress", "http://localhost:8000/wp-admin/install.php") 
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

    [TestMethod]
    public async Task ComposeWaitForHttpShallWork()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      // @formatter:off
      using (new Builder()
                .UseContainer()
                .UseCompose()
                .FromFile(file)
                .RemoveOrphans()
                .WaitForHttp("wordpress",  "http://localhost:8000/wp-admin/install.php", continuation: (resp, cnt) =>  
                             resp.Body.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1 ? 0 : 500)
                .Build().Start())
        // @formatter:on
      {
        // Since we have waited - this shall now always work.       
        var installPage = await "http://localhost:8000/wp-admin/install.php".Wget();
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
    }
    
    [TestMethod]
    [ExpectedException(typeof(FluentDockerException))]
    public void ComposeWaitForHttpThatFailShallBeAborted()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      try
      {
        // @formatter:off
        using (new Builder()
                          .UseContainer()
                          .UseCompose()
                          .FromFile(file)
                          .RemoveOrphans()
                          .WaitForHttp("wordpress",
                                      "http://localhost:8000/wp-admin/install.php", 
                                      continuation: (resp, cnt) =>
                                      {
                                        if (cnt > 3) throw new FluentDockerException($"No Contact after {cnt} times");
                                        return resp.Body.IndexOf("ALIBABA", StringComparison.Ordinal) != -1 ? 0 : 500;
                                      })
                          .Build().Start())
          // @formatter:on

        Assert.Fail("It should have thrown a FluentDockerException!");
      }
      catch
      {
        // Manually remove containers since they are not cleaned up due to the error...
        foreach (var container in new Hosts().Native().GetContainers())
        {
          if (container.Name.StartsWith("wordpress")) container.Dispose();
        }
        
        throw;
      }
    }
  }
}