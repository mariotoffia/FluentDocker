using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Tests.Extensions;
using Ductus.FluentDocker.Tests.MultiContainerTestFiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  [Ignore]
  public class FluentMultiContainerTests
  {
    /// <summary>
    ///   This test is by far completed - needs to have much more support for e.g.
    ///   building a docker file to do this.
    /// </summary>
    /// <remarks>
    ///   As per - http://anandmanisankar.com/posts/docker-container-nginx-node-redis-example/
    /// </remarks>
    [TestMethod]
    public async Task DefineAndBuildImageAddNgixAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      var nginx = Path.Combine(fullPath, "nginx.conf");

      Directory.CreateDirectory(fullPath);
      typeof(NsResolver).ResourceExtract(fullPath, "index.js");

      try
      {
        using (var services = new Builder()

          // Define custom node image to be used
          .DefineImage("mariotoffia/nodetest").ReuseIfAlreadyExists()
          .From("ubuntu:14.04")
          .Maintainer("Mario Toffia <mario.toffia@gmail.com>")
          .WorkingFolder(fullPath)
          .Run("apt-get update &&",
            "apt-get -y install curl &&",
            "curl -sL https://deb.nodesource.com/setup | sudo bash - &&",
            "apt-get -y install python build-essential nodejs")
          .Run("npm install -g nodemon")
          .Add("emb:Ductus.FluentDocker.Tests/Ductus.FluentDocker.Tests.MultiContainerTestFiles/package.txt",
            "/tmp/package.json")
          .Run("cd /tmp && npm install")
          .Run("mkdir -p /src && cp -a /tmp/node_modules /src/")
          .UseWorkDir("/src")
          .Add("index.js", "/src")
          .ExposePorts(8080)
          .Command("nodemon", "/src/index.js").Builder()

          // Redis Db Backend
          .UseContainer().WithName("redis").UseImage("redis").Builder()

          // Node server 1 & 2
          .UseContainer().WithName("node1").UseImage("mariotoffia/nodetest").Link("redis").Builder()
          .UseContainer().WithName("node2").UseImage("mariotoffia/nodetest").Link("redis").Builder()

          // Nginx as load balancer
          .UseContainer().WithName("nginx").UseImage("nginx").Link("node1", "node2")
          .CopyOnStart(nginx, "/etc/nginx/nginx.conf")
          .ExposePort(80).Builder()
          .Build().Start())
        {
          Assert.AreEqual(4, services.Containers.Count);

          var ep = services.Containers.First(x => x.Name == "nginx").ToHostExposedEndpoint("80/tcp");
          Assert.IsNotNull(ep);

          var round1 = await $"http://{ep.Address}:{ep.Port}".Wget();
          Assert.AreEqual("This page has been viewed 1 times!", round1);

          var round2 = await $"http://{ep.Address}:{ep.Port}".Wget();
          Assert.AreEqual("This page has been viewed 2 times!", round2);
        }
      }
      finally
      {
        if (Directory.Exists(fullPath))
        {
          Directory.Delete(fullPath, true);
        }
      }
    }
  }
}