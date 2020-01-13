using System.Collections.Generic;
using System.IO;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Parsers;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ProcessTests
{
  [TestClass]

  public class ProcessEnvironmentTest
  {
    [TestMethod]
    public void ProcessShallPassCustomEnvironment()
    {
      var cmd = "Resources/Scripts/envtest." + (FdOs.IsWindows() ? "bat" : "sh");
      var file = Path.Combine(Directory.GetCurrentDirectory(), (TemplateString)cmd);

      var executor = new ProcessExecutor<StringListResponseParser, IList<string>>(file, string.Empty);
      executor.Env["FD_CUSTOM_ENV"] = "My test environment variable";

      var result = executor.Execute();
      Assert.AreEqual("My test environment variable", result.Data[FdOs.IsWindows() ? 1 : 0]);
    }
  }
}
