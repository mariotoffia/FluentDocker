using Ductus.FluentDocker.Model.Builders.FileBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Model.Builders.FileBuilder
{
  [TestClass]
  public class FileBuilderTest
  {
    [TestMethod]
    public void SimpleConstructor()
    {
      var from  = new FromCommand("mcr.microsoft.com/dotnet/sdk:5.0", "net5.0");
      Assert.AreEqual("FROM mcr.microsoft.com/dotnet/sdk:5.0 AS net5.0", from.ToString());
    }
  }
}