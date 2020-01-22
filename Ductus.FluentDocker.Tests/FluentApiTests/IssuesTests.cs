using System;
using System.Linq;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Tests.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Ductus.FluentDocker.Tests.FluentApiTests
{
  [TestClass]
  public class IssuesTests
  {
    [ClassInitialize]
    public static void Initialize(TestContext ctx) => Utilities.LinuxMode();

    [TestMethod]
    [TestCategory("Issue")]
    public void Issue111_WaitForProcess()
    {
      using (var scope = Fd.EngineScope(EngineScopeType.Windows))
      {
        using (
          var container =
            Fd.DefineImage("mariotoffia/issue111").ReuseIfAlreadyExists()
              .From("mcr.microsoft.com/windows/servercore:ltsc2019")
              .Shell("powershell", "-Command", "$ErrorActionPreference = 'Stop';")
              .Run("Set-ExecutionPolicy Bypass -Scope Process -Force; " +
                   "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12")
              .Run("Invoke-WebRequest -OutFile install.ps1 https://www.chocolatey.org/install.ps1; " +
                   "./install.ps1")
              .Run("choco feature enable --name=allowGlobalConfirmation")
              .Run("choco install python3")
              .Copy("Resources/Issue/111/server.py", "C:/")
              .ExposePorts(8000)
              .Command("python", "server.py")
              .Builder().UseContainer().UseImage("mariotoffia/issue111")
              .WaitForProcess("python.exe", (long)TimeSpan.FromSeconds(30).TotalMilliseconds)
              .Builder()
              .Build()
              .Start())
        {
          var c = container.Containers.First();
          var config = c.GetConfiguration(true);
          AreEqual(ServiceRunningState.Running, config.State.ToServiceState());
        }
      }
    }
  }
}
