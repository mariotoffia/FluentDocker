using System;
using System.Reflection;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ProcessResponseParsersTests
{
  [TestClass]
  public class MinimalNetworkLsResponseParserTests
  {
    [TestMethod]
    public void ProcessShallParseResponse()
    {
      // Arrange
      var id = Guid.NewGuid().ToString();
      var name = Guid.NewGuid().ToString();

      var stdOut = $"{id};{name}";

      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new MinimalNetworkLsResponseParser();

      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.AreEqual(id, result.Id);
      Assert.AreEqual(name, result.Name);
    }
  }
}
