using System;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OperatingSystem = Ductus.FluentDocker.Common.OperatingSystem;

namespace Ductus.FluentDocker.Tests.Model.Common
{
  [TestClass]
  public class TemplateStringTest
  {
    [TestMethod]
    public void UnifiedSeparatorWillBeTranslatedOnWindows()
    {
      if (!OperatingSystem.IsWindows()) return;

      var path = new TemplateString(@"${TEMP}/folder/${RND}");
      
      Assert.IsTrue(path.ToString().IndexOf(@"\folder\", StringComparison.Ordinal) != -1);
    }
  }
}