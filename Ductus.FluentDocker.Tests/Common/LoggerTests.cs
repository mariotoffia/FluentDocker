using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Common
{
  [TestClass]
  public class LoggerTests
  {
    [TestMethod]
    public void ShouldNotLogToTraceListenerWhenLoggerIsDisabled()
    {
      Logging.Disabled();

      // Arrange
      var messages = new List<string>
      {
        "message 1",
        "message 2",
        "message 3",
      };

      var actualTraceMessages = new List<string>();

      Trace.Listeners.Add(new UnitTestingTraceListener
      {
        OnWriteLine = actualTraceMessages.Add
      });

      // Act
      messages.ForEach(Logger.Log);
      Trace.Flush();

      // Assert
      Assert.IsFalse(actualTraceMessages.Any());
    }

    [TestMethod]
    public void ShouldLogToTraceListenerWhenLoggerIsEnabled()
    {
      Logging.Enabled();

      // Arrange
      var messages = new List<string>
      {
        "message 1",
        "message 2",
        "message 3",
      };

      var expectedTraceMessages = messages
                                    .Select(message => $"Ductus.FluentDocker: {message}")
                                    .ToList();

      var actualTraceMessages = new List<string>();

      Trace.Listeners.Add(new UnitTestingTraceListener
      {
        OnWriteLine = actualTraceMessages.Add
      });

      // Act
      messages.ForEach(Logger.Log);
      Trace.Flush();

      // Assert
      CollectionAssert.AreEqual(expectedTraceMessages, actualTraceMessages);
    }
  }
}
