using System;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OperatingSystem = Ductus.FluentDocker.Common.OperatingSystem;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Tests.Model.Common
{
  [TestClass]
  public class TemplateStringTests
  {
    [TestMethod]
    public void UnifiedSeparatorWillBeTranslatedOnWindows()
    {
      if (!OperatingSystem.IsWindows()) return;

      var path = new TemplateString(@"${TEMP}/folder/${RND}");
      
      Assert.IsTrue(path.ToString().IndexOf(@"\folder\", StringComparison.Ordinal) != -1);
    }

    [TestMethod]
    public void SpacesInTemplateStringIsEscaped()
    {
      var path = new TemplateString(@"${TEMP}/folder with space/${RND}");
      Assert.IsTrue(path.EscapePath().ToString().StartsWith("\""));
    }

    [TestMethod]
    public void NoSpacesInTemplateStringIsNotEscaped()
    {
      var path = new TemplateString(@"${TEMP}/folder/${RND}");
      Assert.IsFalse(path.EscapePath().ToString().StartsWith("\""));
    }
  }
}