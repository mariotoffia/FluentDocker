using System;
using FluentDocker.Common;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public class TemplateStringTests
  {
    [Fact]
    public void TempVariable_IsRendered()
    {
      var path = new TemplateString(@"${TEMP}/folder/${RND}");

      Assert.NotNull(path.Rendered);
      Assert.DoesNotContain("${TEMP}", path.Rendered);
      Assert.DoesNotContain("${RND}", path.Rendered);
    }

    [Fact]
    public void TmpVariable_IsRendered()
    {
      var path = new TemplateString(@"${TMP}/folder");

      Assert.NotNull(path.Rendered);
      Assert.DoesNotContain("${TMP}", path.Rendered);
    }

    [Fact]
    public void TmpVariable_CustomTempPathWithoutTrailingSeparator_DoesNotTrimLastCharacter()
    {
      var original = DirectoryHelper.GetTempPath;
      try
      {
        DirectoryHelper.GetTempPath = () => "/custom/temp";

        var path = new TemplateString(@"${TMP}/folder");

        Assert.Equal("/custom/temp/folder", path.Rendered);
      }
      finally
      {
        DirectoryHelper.GetTempPath = original;
      }
    }

    [Fact]
    public void PwdVariable_IsRendered()
    {
      var path = new TemplateString(@"${PWD}/myfile.txt");

      Assert.NotNull(path.Rendered);
      Assert.DoesNotContain("${PWD}", path.Rendered);
      Assert.Contains(Environment.CurrentDirectory, path.Rendered);
    }

    [Fact]
    public void RndVariable_IsRendered_ToRandomFileName()
    {
      var path1 = new TemplateString(@"${RND}");
      var path2 = new TemplateString(@"${RND}");

      // Should not contain the template anymore
      Assert.DoesNotContain("${RND}", path1.Rendered);
      Assert.DoesNotContain("${RND}", path2.Rendered);

      // Should be random (different each time)
      Assert.NotEqual(path1.Rendered, path2.Rendered);
    }

    [Fact]
    public void RndVariable_MultipleOccurrences_RenderIndependently()
    {
      var path = new TemplateString(@"${RND}/${RND}");

      var parts = path.Rendered.Split('/');

      Assert.Equal(2, parts.Length);
      Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void UrlAtBeginningOfString_NotAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return; // Skip on non-Windows

      var tst = new TemplateString("\"https://kalle.hoobe.net/hejohå.txt\" ${TEMP}/folder/${RND}");
      Assert.StartsWith("\"https://kalle.hoobe.net/hejohå.txt\"", tst.Rendered);
    }

    [Fact]
    public void UrlAtEndOfString_NotAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return; // Skip on non-Windows

      var tst = new TemplateString("/foo/bar/cmd \"https://kalle.hoobe.net/hejohå.txt\"");
      Assert.Contains("\"https://kalle.hoobe.net/hejohå.txt\"", tst.Rendered);
    }

    [Fact]
    public void UrlInPowershellExpression_NotAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return; // Skip on non-Windows

      var tst = new TemplateString("RUN Set-ExecutionPolicy Bypass -Scope Process -Force; iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))");
      Assert.Contains("'https://chocolatey.org/install.ps1'", tst.Rendered);
    }

    [Fact]
    public void MultipleUrls_NotAlteredOnWindows()
    {
      if (!FdOs.IsWindows())
        return; // Skip on non-Windows

      var tst = new TemplateString("/foo/bar/cmd \"https://kalle.hoobe.net/hejohå.txt\" \"http://kalle.hoobe.net/hejohå.txt\"");
      Assert.Contains("\"https://kalle.hoobe.net/hejohå.txt\"", tst.Rendered);
      Assert.Contains("\"http://kalle.hoobe.net/hejohå.txt\"", tst.Rendered);
    }

    [Fact]
    public void MixedDockerVolumeSpec_KeepsForwardSlashesOnAllOses()
    {
      var path = new TemplateString("c:/data:/data");
      Assert.Equal("c:/data:/data", path.Rendered);
    }

    [Fact]
    public void HostMountTemplateSpec_ContainerSideKeepsForwardSlashes()
    {
      var path = new TemplateString(@"${TMP}/data:/data");
      var containerSide = path.Rendered[(path.Rendered.LastIndexOf(':') + 1)..];

      Assert.Equal("/data", containerSide);
      Assert.DoesNotContain('\\', containerSide);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void SpacesInPath_AreEscapedCorrectly()
    {
      var path = new TemplateString(@"${TEMP}/folder with space/${RND}");
      var escaped = path.EscapePath();

      // Paths with spaces should be quoted
      Assert.StartsWith("\"", escaped.Rendered);
      Assert.EndsWith("\"", escaped.Rendered);
    }

    [Fact]
    [Obsolete("Exercises an obsolete API on purpose; the attribute suppresses CS0618 at the call site.")]
    public void NoSpacesInPath_NotEscaped()
    {
      var path = new TemplateString(@"${TEMP}/folder/${RND}");
      var escaped = path.EscapePath();

      // Paths without spaces should not be quoted
      Assert.False(escaped.Rendered.StartsWith('"'));
    }

    [Fact]
    public void ImplicitConversion_FromString()
    {
      TemplateString ts = "/foo/bar/${RND}";
      Assert.NotNull(ts);
      Assert.DoesNotContain("${RND}", ts.Rendered);
    }

    [Fact]
    public void ImplicitConversion_ToString()
    {
      var ts = new TemplateString("/foo/bar");
      string rendered = ts;
      Assert.Equal("/foo/bar", rendered);
    }

    [Fact]
    public void Original_PreservesInputString()
    {
      var input = "${TEMP}/folder/${RND}";
      var ts = new TemplateString(input);

      Assert.Equal(input, ts.Original);
      Assert.NotEqual(ts.Original, ts.Rendered);
    }

    [Fact]
    public void NullString_ImplicitConversion_ReturnsNull()
    {
      string? nullString = null;
      TemplateString ts = nullString!; // intentional null to verify null-handling
      Assert.Null(ts);
    }

    [Fact]
    public void Constructor_NullString_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() => new TemplateString(null!));
    }

    [Fact]
    public void EnvironmentVariable_IsRendered()
    {
      // Set a test environment variable
      var testKey = string.Concat("FD_TEST_VAR_", Guid.NewGuid().ToString("N").AsSpan(0, 8));
      var testValue = "test_value_123";

      try
      {
        Environment.SetEnvironmentVariable(testKey, testValue);
        var ts = new TemplateString($"prefix_${{E_{testKey}}}_suffix");

        Assert.Contains(testValue, ts.Rendered);
        Assert.DoesNotContain($"${{E_{testKey}}}", ts.Rendered);
      }
      finally
      {
        Environment.SetEnvironmentVariable(testKey, null);
      }
    }

    [Fact]
    public void MissingEnvironmentVariable_IsLeftUnresolved()
    {
      var key = string.Concat("FD_MISSING_", Guid.NewGuid().ToString("N"));
      Environment.SetEnvironmentVariable(key, null);

      var ts = new TemplateString($"prefix_${{E_{key}}}_suffix");

      Assert.Equal($"prefix_${{E_{key}}}_suffix", ts.Rendered);
    }

    [Fact]
    public void Equality_UsesRenderedValue()
    {
      Assert.Equal(new TemplateString("/same"), new TemplateString("/same"));
      Assert.True(new TemplateString("/same") == new TemplateString("/same"));
      Assert.NotEqual(new TemplateString("/same"), new TemplateString("/other"));
    }

    [Fact]
    public void EmbeddedResourcePath_NotAltered()
    {
      var ts = new TemplateString("emb:MyAssembly/Resources/file.txt");

      // emb: paths should not be altered
      Assert.Equal("emb:MyAssembly/Resources/file.txt", ts.Rendered);
    }
  }
}
