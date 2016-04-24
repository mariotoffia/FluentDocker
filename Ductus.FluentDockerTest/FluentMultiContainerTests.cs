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
    [TestMethod]
    public void WeaveCluster()
    {
      var fullPath = (TemplateString) @"${TEMP}\fluentdockertest\${RND}";
      var app = Path.Combine(fullPath, "index.js");
      var nginx = Path.Combine(fullPath, "nginx.conf");

      Directory.CreateDirectory(fullPath);
      typeof(NsResolver).Namespace.ExtractEmbeddedResource(null, fullPath, "index.js", "nginx.conf");

      // TODO: Need to implement run command in container ...
      try
      {
        // As per:
        // http://anandmanisankar.com/posts/docker-container-nginx-node-redis-example/
        // TODO: Currently it does not resolve links and only starts container
        // TODO: in order they where added in fluent API - thus bottom up approach
        //
        using (var services = new Builder()
          // Redis Db Backend
          .UseContainer().WithName("redis").UseImage("redis").Builder()
          // Node server 1
          .UseContainer().WithName("node1").UseImage("node")
          .Link("redis").CopyOnStart(app, "/usr/src/app").UseWorkDir("/usr/src/app")
          .Command("nodemon", "/usr/src/app").Builder()
          // Node server 1
          .UseContainer().WithName("node2").UseImage("node")
          .Link("redis").CopyOnStart(app, "/usr/src/app").UseWorkDir("/usr/src/app")
          .Command("nodemon", "/usr/src/app").Builder()
          // Nginx as load balancer
          .UseContainer().WithName("nginx").UseImage("nginx")
          .Link("node1", "node2").CopyOnStart(nginx, "/etc/nginx/nginx.conf").ExposePort(80).Builder()
          .Build().Start())
        {
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