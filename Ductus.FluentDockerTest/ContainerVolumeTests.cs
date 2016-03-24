using Ductus.FluentDocker;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ContainerVolumeTests
  {
    /// <summary>
    ///   Need to find a more suitable container since pg can create directory but not link data files...
    ///   But the volumes are shared between container and host but the process within docker do not
    ///   have privileges to access it.
    /// </summary>
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
            .Build())
      {
        container.Start();
      }
    }
  }
}