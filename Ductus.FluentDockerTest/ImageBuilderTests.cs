using System;
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
      using (var image = new Builder().UseContainer().Build())
      {
        
      }
    }
  }
}
