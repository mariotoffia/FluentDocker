using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

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
    public void BuildImageShallPerserveLineOrdering()
    {
      var dockerfile = Fd.Dockerfile()
        .UseParent("mcr.microsoft.com/windows/servercore:ltsc2019")
        .Shell("powershell", "-Command", "$ErrorActionPreference = 'Stop';")
        .Run("Set-ExecutionPolicy Bypass -Scope Process -Force; " +
              "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12")
        .Run("Invoke-WebRequest -OutFile install.ps1 https://www.chocolatey.org/install.ps1; " +
              "./install.ps1")
        .Run("choco feature enable --name=allowGlobalConfirmation")
        .Run("choco install python3")
        .Copy("Resources/Issue/111/server.py", "C:/")
        .ExposePorts(8000)
        .Command("python", "server.py").ToDockerfileString();

      var lines = dockerfile.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
      Assert.AreEqual(10, lines.Length);
      Assert.AreEqual("FROM mcr.microsoft.com/windows/servercore:ltsc2019", lines[0]);
      Assert.AreEqual("SHELL [\"powershell-Command\",\"$ErrorActionPreference = 'Stop';\"]", lines[1]);
      Assert.AreEqual("RUN Set-ExecutionPolicy Bypass -Scope Process -Force; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12", lines[2]);
      Assert.AreEqual("RUN Invoke-WebRequest -OutFile install.ps1 https://www.chocolatey.org/install.ps1; ./install.ps1", lines[3]);
      Assert.AreEqual("RUN choco feature enable --name=allowGlobalConfirmation", lines[4]);
      Assert.AreEqual("RUN choco install python3", lines[5]);
      Assert.AreEqual("COPY Resources/Issue/111/server.py C:/", lines[6]);
      Assert.AreEqual("EXPOSE 8000", lines[7]);
      Assert.AreEqual("CMD [\"pythonserver.py\"]", lines[8]);
      Assert.AreEqual(string.Empty, lines[9]);
    }

    [TestMethod]
    [Ignore]
    public void BuildImageFromFileWithCopyAndRunInstructionShallWork()
    {
      using (
        var image =
          Fd.DefineImage("mariotoffia/unittest:latest")
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
        Assert.AreEqual(2, config.Config.Cmd.Length);
        Assert.AreEqual("/usr/bin/node", config.Config.Cmd[0]);
        Assert.AreEqual("/var/www/app.js", config.Config.Cmd[1]);
      }
    }
  }
}
