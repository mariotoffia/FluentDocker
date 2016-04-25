using System.IO;
using System.Linq;
using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDockerTest.MultiContainerTestFiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
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
    public void WeaveCluster()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      var nginx = Path.Combine(fullPath, "nginx.conf");

      Directory.CreateDirectory(fullPath);
      typeof(NsResolver).Namespace.ExtractEmbeddedResource(null, fullPath, "index.js", "nginx.conf");

      try
      {
        using (var services = new Builder()
          // Define custom node image to be used
          .DefineImage("mariotoffia/nodetest")
          .ReuseIfAlreadyExists()
          .DefineFrom("ubuntu").Maintainer("Mario Toffia <mario.toffia@gmail.com>")
          .Run("apt-get update &&",
            "apt-get -y install curl &&",
            "curl -sL https://deb.nodesource.com/setup | sudo bash - &&",
            "apt-get -y install python build-essential nodejs")
          .Run("npm install -g nodemon")
          .Add("embedded:Ductus.FluentDockerTest/Ductus.FluentDockerTest.MultiContainerTestFiles/package.txt",
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
          .UseContainer().WithName("nginx").UseImage("nginx")
          .Link("node1", "node2").CopyOnStart(nginx, "/etc/nginx/nginx.conf").ExposePort(80).Builder()
          .Build().Start())
        {
          Assert.AreEqual(4, services.Containers.Count);

          var ep = services.Containers.First(x => x.Name == "nginx").ToHostExposedEndpoint("80/tcp");
          Assert.IsNotNull(ep);
          // TODO: Curl on ep and verify counter (thus nginx->nodeX->redis)
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