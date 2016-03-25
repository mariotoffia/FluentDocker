using System;
using System.IO;
using System.Net;
using Ductus.FluentDocker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ContainerVolumeTests
  {
    private const string Html = "<html><head>Hello World</head><body><h1>Hello world</h1></body></html>";

    [TestMethod]
    public void TestBindReadWriteSingleVolumeOnNgix()
    {
      using (
        var container =
          new DockerBuilder()
            .WithImage("nginx:latest")
            .ExposePorts("80")
            .WaitForPort("80/tcp", 10000 /*10s*/)
            .MountNamedVolume("test", "${TEMP}/fluentdockertest/${RND}", "/usr/share/nginx/html", "ro")
            .WhenDisposed()
            .RemoveVolume("${TEMP}/fluentdockertest")
            .Build().Start())
      {
        var hostdir = container.GetHostVolume("test");
        Assert.IsNotNull(hostdir);
        Assert.IsTrue(Directory.Exists(hostdir));

        File.WriteAllText(Path.Combine(hostdir, "hello.html"), Html);
 
        var request = WebRequest.Create($"http://{container.Host}:{container.GetHostPort("80/tcp")}/hello.html");
        using (var response = request.GetResponse())
        {
          Assert.AreEqual("OK", ((HttpWebResponse)response).StatusDescription);
          var dataStream = response.GetResponseStream();
          Assert.IsNotNull(dataStream);

          using (var reader = new StreamReader(dataStream))
          {
            var responseFromServer = reader.ReadToEnd();
            Assert.AreEqual(Html, responseFromServer);
          }
        }
      }
    }
  }
}