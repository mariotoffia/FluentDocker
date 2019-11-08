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
    public void UrlAtTheBeginningOfStringShallNotBeAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return;

      var tst = new TemplateString("\"https://kalle.hoobe.net/hejohå.txt\" ${TEMP}/folder/${RND}");
      Assert.IsTrue(tst.Rendered.IndexOf("\"https://kalle.hoobe.net/hejohå.txt\"", StringComparison.Ordinal) == 0);
    }

    [TestMethod]
    public void UrlAtTheEndOfStringShallNotBeAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return;

      var tst = new TemplateString("/foo/bar/cmd \"https://kalle.hoobe.net/hejohå.txt\"");
      Assert.IsTrue(tst.Rendered.IndexOf("\"https://kalle.hoobe.net/hejohå.txt\"", StringComparison.Ordinal) == 13);
    }

    [TestMethod]
    public void UrlWithinAPowershellExpressionOnWindowsShallLeaveUrlUntouched()
    {
      if (!FdOs.IsWindows())
        return;

      var tst = new TemplateString("RUN Set-ExecutionPolicy Bypass -Scope Process -Force; iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))");
      Assert.IsTrue(tst.Rendered.IndexOf("'https://chocolatey.org/install.ps1'", StringComparison.Ordinal) == 108);
    }

    [TestMethod]
    public void IsIsPossibleToHaveSeveralUrlsInSameStringNotAffectingWindowsPathSubstitution()
    {
      if (!FdOs.IsWindows())
        return;

      var tst = new TemplateString("/foo/bar/cmd \"https://kalle.hoobe.net/hejohå.txt\" \"http://kalle.hoobe.net/hejohå.txt\"");
      var res = tst.Rendered.IndexOf("\"http://kalle.hoobe.net/hejohå.txt\"", StringComparison.Ordinal);
      Assert.IsTrue(tst.Rendered.IndexOf("\"https://kalle.hoobe.net/hejohå.txt\"", StringComparison.Ordinal) == 13);
      Assert.IsTrue(tst.Rendered.IndexOf("\"http://kalle.hoobe.net/hejohå.txt\"", StringComparison.Ordinal) == 50);
    }

    [TestMethod]
    public void UnifiedSeparatorWillBeTranslatedOnWindows()
    {
      if (!FdOs.IsWindows())
        return;

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
