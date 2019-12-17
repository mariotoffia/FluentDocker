using System;
using Ductus.FluentDocker.Model.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Model.Containers
{
  [TestClass]
  public class ContainerCreateParamsTests
  {
    [TestMethod]
    public void NvidiaRuntimeGeneratesRuntimeOption()
    {
      var prms = new ContainerCreateParams { Runtime = ContainerRuntime.Nvidia };
      var opts = prms.ToString();
      Assert.IsTrue(opts.Contains(" --runtime=nvidia"));
    }
  }
}
