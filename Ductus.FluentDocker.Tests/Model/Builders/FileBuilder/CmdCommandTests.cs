using Ductus.FluentDocker.Model.Builders.FileBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Model.Builders.FileBuilder
{
  [TestClass]
  public class FileBuilderTest
  {
    [TestMethod]
    public void FromWithAliasShallRenderUsingAS()
    {
      var from  = new FromCommand("mcr.microsoft.com/dotnet/sdk:5.0", "net5.0");
      Assert.AreEqual("FROM mcr.microsoft.com/dotnet/sdk:5.0 AS net5.0", from.ToString());
    }

    [TestMethod]
    public void ShellShallHaveCommandAndArgsSeparated()
    {
      var shell  = new ShellCommand("cmd", "/S", "/C");
      Assert.AreEqual("SHELL [\"cmd\",\"/S\",\"/C\"]", shell.ToString());
    }

    [TestMethod]
    public void ShellShallSingleCommandNoArgument()
    {
      var shell  = new ShellCommand("cmd");
      Assert.AreEqual("SHELL [\"cmd\"]", shell.ToString());
    }

    [TestMethod]
    public void ShellShallSingleCommandOneArgument()
    {
      var shell  = new ShellCommand("cmd","/S");
      Assert.AreEqual("SHELL [\"cmd\",\"/S\"]", shell.ToString());
    }
  }
}