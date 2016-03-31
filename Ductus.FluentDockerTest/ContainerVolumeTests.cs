using System.IO;
using System.Linq;
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
          Assert.AreEqual("OK", ((HttpWebResponse) response).StatusDescription);
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

    [TestMethod]
    public void CopyBinDirToHostManually()
    {
      using (
        var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("postgres:latest")
            .Build().Start())
      {
        var path = container.Copy("/bin", "${TEMP}/fluentdockertest/${RND}");

        var files = Directory.EnumerateFiles(Path.Combine(path, "bin")).ToArray();
        Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
        Assert.IsTrue(files.Any(x => x.EndsWith("ps")));
        Assert.IsTrue(files.Any(x => x.EndsWith("zcat")));
      }
    }

    [TestMethod]
    public void CopyBinDirToHostBeforeStarting()
    {
      using (
        var container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("postgres:latest")
            .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}", "test")
            .Build().Start())
      {
        var path = container.GetHostCopyPath("test");

        var files = Directory.EnumerateFiles(Path.Combine(path, "bin")).ToArray();
        Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
        Assert.IsTrue(files.Any(x => x.EndsWith("ps")));
        Assert.IsTrue(files.Any(x => x.EndsWith("zcat")));
      }
    }

    [TestMethod]
    public void CopyBinDirToHostBeforeDisposed()
    {
      DockerContainer container;
      using (
        container =
          new DockerBuilder()
            .WithEnvironment("POSTGRES_PASSWORD=mysecretpassword")
            .WithImage("postgres:latest")
            .WhenDisposed()
              .CopyFromContainer("/bin", "${TEMP}/fluentdockertest/${RND}","test")
            .Build().Start())
      {
      }

      var path = container.GetHostCopyPath("test");

      var files = Directory.EnumerateFiles(Path.Combine(path, "bin")).ToArray();
      Assert.IsTrue(files.Any(x => x.EndsWith("bash")));
      Assert.IsTrue(files.Any(x => x.EndsWith("ps")));
      Assert.IsTrue(files.Any(x => x.EndsWith("zcat")));

    }
  }
}