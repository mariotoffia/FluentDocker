using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Services.Impl;
using Ductus.FluentDocker.Tests.Extensions;
using Ductus.FluentDocker.Services.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
// ReSharper disable StringLiteralTypo

namespace Ductus.FluentDocker.Tests.ServiceTests
{
  [TestClass]
  public class DockerComposeTests : FluentDockerTestBase
  {
    [TestMethod]
    public async Task WordPressDockerComposeServiceShallShowInstallScreen()
    {
      var file = Path.Combine(Directory.GetCurrentDirectory(),
        (TemplateString) "Resources/ComposeTests/WordPress/docker-compose.yml");

      using (var svc = new DockerComposeCompositeService(DockerHost, new DockerComposeConfig
      {
        ComposeFilePath = new List<string> { file }, ForceRecreate = true, RemoveOrphans = true,
        StopOnDispose = true
      }))
      {
        svc.Start();

        svc.Containers.First(x => x.Name == "wordpress").WaitForHttp("http://localhost:8000/wp-admin/install.php");
        
        // We now have a running WordPress with a MySql database
        var installPage = await $"http://localhost:8000/wp-admin/install.php".Wget();
        
        Assert.IsTrue(installPage.IndexOf("https://wordpress.org/", StringComparison.Ordinal) != -1);
      }
    }
  }
}