using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class ImageBuilderTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx)
    {
      Utilities.LinuxMode();
    }

    [TestMethod]
    [Ignore]
    public void BuildImageFromFileWithCopyAndRunInstructionShallWork()
    {
      using (
        var image =
          new Builder().DefineImage("mariotoffia/unittest:latest")
            .From("ubuntu:14.04")
            .Maintainer("Mario Toffia <mario.toffia@gmail.com>")
              .Run("apt-get update")
              .Run("apt-get install -y software-properties-common python")
              .Run("add-apt-repository ppa:chris-lea/node.js")
              .Run("echo \"deb http://us.archive.ubuntu.com/ubuntu/ precise universe\" >> /etc/apt/sources.list")
              .Run("apt-get update")
              .Run("apt-get install -y nodejs")
              .Run("mkdir /var/www")
              .Add("emb:Ductus.FluentDocker.Tests/Ductus.FluentDocker.Tests.MultiContainerTestFiles/app.js", "/var/www/app.js")
            .Command("/usr/bin/node", "/var/www/app.js")
            .Build())
      {
        var config = image.GetConfiguration(true);
        Assert.IsNotNull(config);
        Assert.AreEqual(2,config.Config.Cmd.Length);
        Assert.AreEqual("/usr/bin/node", config.Config.Cmd[0]);
        Assert.AreEqual("/var/www/app.js", config.Config.Cmd[1]);
      }
    }
  }
}