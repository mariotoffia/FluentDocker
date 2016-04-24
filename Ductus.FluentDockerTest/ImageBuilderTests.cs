using Ductus.FluentDocker.Builders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDockerTest
{
  [TestClass]
  public class ImageBuilderTests
  {
    [TestMethod]
    public void BuildImageFromFileWithCopyAndRunInstructionShallWork()
    {
      using (
        var image =
          new Builder().DefineImage("mariotoffia/unittest:latest")
            .DefineFrom("ubuntu")
            .Maintainer("Mario Toffia <mario.toffia@gmail.com>")
            .Run("apt-get install -y software-properties-common python")
            .Run("add-apt-repository ppa:chris-lea/node.js")
            .Run("echo \"deb http://us.archive.ubuntu.com/ubuntu/ precise universe\" >> /etc/apt/sources.list")
            .Run("apt-get update")
            .Run("apt-get install -y nodejs")
            .Run("mkdir /var/www")
            .Add("embedded://Ductus.FluentDockerTest/MultiContainerTestFiles/app.js", "/var/www/app.js")
            .Command("/usr/bin/node", "/var/www/app.js")
            .Build())
      {
        var config = image.GetConfiguration(true);
      }
    }
  }
}