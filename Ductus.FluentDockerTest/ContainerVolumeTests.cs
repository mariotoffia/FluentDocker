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
            .MountVolumes(@"${TEMP}/fluentdockertest/${RND}:/usr/share/nginx/html:ro")
            .WhenDisposed()
              .RemoveVolume("${TEMP}/fluentdockertest")
            .Build().Start())
      {
      }
    }
  }
}