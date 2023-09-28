using System;
using System.Reflection;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ProcessResponseParsersTests
{
  [TestClass]
  public class NetworkLsResponseParserTests
  {
    [TestMethod]
    public void ProcessShallParseResponse()
    {
      // Arrange

      var id = Guid.NewGuid().ToString();
      var name = Guid.NewGuid().ToString();
      var driver = Guid.NewGuid().ToString();
      var scope = Guid.NewGuid().ToString();
      var ipv6 = false;
      var isInternal = true;
      var created = DateTime.Now.ToUniversalTime();

      var stdOut = $"{id};{name};{driver};{scope};{ipv6};{isInternal};{created:yyyy-MM-dd HH:mm:ss.ffffff} +0000 ZZZ";

      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new NetworkLsResponseParser();


      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.AreEqual(id, result.Id);
      Assert.AreEqual(name, result.Name);
    }


    [TestMethod]
    public void ProcessShallParseResponseWithNegativeTimezone()
    {
      // Arrange

      var tzShift = -3;
      var id = Guid.NewGuid().ToString();
      var name = Guid.NewGuid().ToString();
      var driver = Guid.NewGuid().ToString();
      var scope = Guid.NewGuid().ToString();
      var ipv6 = false;
      var isInternal = true;
      var created = DateTime.Now;

      var stdOut = $"{id};{name};{driver};{scope};{ipv6};{isInternal};{created:yyyy-MM-dd HH:mm:ss.ffffff} {tzShift:00}00 ZZZ";

      var ctorArgs = new object[] { "command", stdOut, "", 0 };
      var executionResult = (ProcessExecutionResult)Activator.CreateInstance(typeof(ProcessExecutionResult),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
        null, ctorArgs, null, null);

      var parser = new NetworkLsResponseParser();


      // Act
      var result = parser.Process(executionResult).Response.Data[0];

      // Assert
      Assert.AreEqual(id, result.Id);
      Assert.AreEqual(name, result.Name);
    }
  }
}
