using System.Linq;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.CommandTests
{
  [TestClass]
  public sealed class ImageTests : FluentDockerTestBase
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestMethod]
    public void ImageConfigurationShallBeRetrievable()
    {
      var result = DockerHost.Host.Pull("postgres:10-alpine");
      Assert.IsTrue(result.Success);

      var config = DockerHost.GetImages().First(x => x.Name == "postgres").GetConfiguration(true);
      Assert.IsNotNull(config);
    }

    [TestMethod]
    public void ImageIsExposedOnARunningContainer()
    {
      using (var container = DockerHost.Create("postgres:9.6-alpine", false,
        new ContainerCreateParams
        {
          Environment = new[] { "POSTGRES_PASSWORD=mysecretpassword" }
        }))
      {
        var config = container.GetConfiguration();
        var image = container.Image.GetConfiguration();

        Assert.AreEqual(config.Image, image.Id);
      }
    }
  }
}
