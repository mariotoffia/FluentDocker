using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Tests.Model.Common
{
  [TestClass]
  public class TemplateStringTests
  {
    [TestMethod]
    public void UnifiedSeparatorWillBeTranslatedOnWindows()
    {
      if (!FdOs.IsWindows()) return;

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