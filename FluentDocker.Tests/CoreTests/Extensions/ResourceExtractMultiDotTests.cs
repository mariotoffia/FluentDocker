using System;
using System.IO;
using FluentDocker.Extensions;
using Xunit;

namespace Res
{
  /// <summary>
  /// Marker type whose namespace ("Res") matches the LogicalName root of the embedded multi-dot
  /// fixtures in <c>Fixtures/ResourceQuery/Res/</c> (e.g. "Res.Dockerfile.template"), so
  /// <c>typeof(ResMarker).ResourceExtract(...)</c> resolves <see cref="Type.Namespace" /> to "Res"
  /// and queries exactly that manifest prefix.
  /// </summary>
  internal static class ResMarker
  {
  }
}

namespace FluentDocker.Tests.CoreTests.Extensions
{
  /// <summary>
  /// PROVEN-LIVE reproductions for Co-H1 (<c>ResourceExtract(files)</c> silently extracts nothing for
  /// multi-dot filenames) and Co-M1 (extraction mangles multi-dot filenames into a subdirectory +
  /// fragment instead of a file), using real embedded resources under the "Res" manifest namespace.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ResourceExtractMultiDotTests
  {
    private const string DockerfileTemplateContent = "FROM {{BASE_IMAGE}}\nRUN echo multidot\n";
    private const string PlainTextContent = "plain control content\n";
    private const string SettingsProductionContent = "env=production\nreplicas=3\n";
    private const string ArchiveTarGzContent = "fake-tar-gz-payload\n";
    private const string AppPropertiesContent = "key=value\ntimeout=30\n";
    private const string SubDockerfileTemplateContent = "FROM {{BASE_IMAGE}}\nRUN echo sub-nested\n";

    [Fact]
    public void ResourceExtract_MultiDotFilename_ExtractsFileWithExactBytes()
    {
      // Co-H1 PROVEN-LIVE repro: before the fix this directory stayed empty (silent no-op, no
      // exception) because the non-recursive query dropped "Res.Dockerfile.template" before
      // Include's suffix-match ever ran.
      var dir = NewOutDir(nameof(ResourceExtract_MultiDotFilename_ExtractsFileWithExactBytes));
      try
      {
        typeof(Res.ResMarker).ResourceExtract(dir, "Dockerfile.template");

        var expectedFile = Path.Combine(dir, "Dockerfile.template");
        Assert.True(File.Exists(expectedFile), $"Expected file at {expectedFile}");
        Assert.Equal(DockerfileTemplateContent, File.ReadAllText(expectedFile));

        // Co-M1 PROVEN-LIVE repro: the resource must land as a FILE named "Dockerfile.template",
        // never scattered into a "Dockerfile" directory containing a "template" fragment.
        Assert.False(Directory.Exists(Path.Combine(dir, "Dockerfile")),
          "Dockerfile.template must not be mangled into a Dockerfile/template subdirectory + fragment.");
      }
      finally
      {
        Cleanup(dir);
      }
    }

    [Fact]
    public void ResourceExtract_PlainSingleDotFilename_StillExtractsControl()
    {
      // Control: guards against over-fixing — a normal single-extension file must keep working.
      var dir = NewOutDir(nameof(ResourceExtract_PlainSingleDotFilename_StillExtractsControl));
      try
      {
        typeof(Res.ResMarker).ResourceExtract(dir, "plain.txt");

        AssertFile(dir, "plain.txt", PlainTextContent);
      }
      finally
      {
        Cleanup(dir);
      }
    }

    [Theory]
    [InlineData("settings.production")]
    [InlineData("archive.tar.gz")]
    [InlineData("app.properties")]
    public void ResourceExtract_MultiDotFamily_ExtractsByRequestedName(string requested)
    {
      var dir = NewOutDir(nameof(ResourceExtract_MultiDotFamily_ExtractsByRequestedName) + "-" + requested);
      try
      {
        typeof(Res.ResMarker).ResourceExtract(dir, requested);

        var expectedContent = requested switch
        {
          "settings.production" => SettingsProductionContent,
          "archive.tar.gz" => ArchiveTarGzContent,
          "app.properties" => AppPropertiesContent,
          _ => throw new InvalidOperationException($"Unexpected inline data: {requested}")
        };
        AssertFile(dir, requested, expectedContent);
      }
      finally
      {
        Cleanup(dir);
      }
    }

    [Fact]
    public void ResourceExtract_RequestedNameAtTwoNestingDepths_RewritesBothCorrectly()
    {
      // Exercises MatchRequestedResource's "prefix != Root" branch: the SAME requested leaf name
      // exists both directly under the query root ("Res.Dockerfile.template") and one level deeper
      // ("Res.Sub.Dockerfile.template"); a single recursive Include(...) call must extract both, to
      // their own distinct, correctly-reconstructed relative folders.
      var dir = NewOutDir(nameof(ResourceExtract_RequestedNameAtTwoNestingDepths_RewritesBothCorrectly));
      try
      {
        typeof(Res.ResMarker).ResourceExtract(dir, "Dockerfile.template");

        AssertFile(dir, "Dockerfile.template", DockerfileTemplateContent);
        AssertFile(dir, Path.Combine("Sub", "Dockerfile.template"), SubDockerfileTemplateContent);
      }
      finally
      {
        Cleanup(dir);
      }
    }

    [Fact]
    public void ResourceExtract_NoFiles_RecursiveExtractsMultiDotFilesAsFilesAtRoot()
    {
      // No-files path (ResourceQuery().ToFile()) is inherently lossy: there is no requested name to
      // disambiguate namespace-dots from filename-dots. This documents the GUARANTEE this task adds:
      // a multi-dot resource whose ExtractFile guess is dotless (Dockerfile.template,
      // settings.production, app.properties) now lands as one correctly-named FILE at the target
      // root instead of a mangled subdirectory + fragment.
      var dir = NewOutDir(nameof(ResourceExtract_NoFiles_RecursiveExtractsMultiDotFilesAsFilesAtRoot));
      try
      {
        typeof(Res.ResMarker).ResourceExtract(dir);

        AssertFile(dir, "Dockerfile.template", DockerfileTemplateContent);
        AssertFile(dir, "settings.production", SettingsProductionContent);
        AssertFile(dir, "app.properties", AppPropertiesContent);
        AssertFile(dir, "plain.txt", PlainTextContent);

        // Documented remaining ambiguity: a compound SHORT extension (".tar.gz") already parses via
        // ExtractFile's normal 2-segment walk-back into a DOTTED guess ("tar.gz"), so the no-files
        // path's refinement (which only re-anchors DOTLESS guesses) does not touch it — it lands one
        // folder off ("archive/tar.gz") instead of at the root as "archive.tar.gz". It is still a
        // correctly named FILE, never a directory+fragment. Callers who need the exact path should
        // request it by name (see ResourceExtract_MultiDotFamily_ExtractsByRequestedName), which is
        // exact regardless of this heuristic.
        AssertFile(dir, Path.Combine("archive", "tar.gz"), ArchiveTarGzContent);
      }
      finally
      {
        Cleanup(dir);
      }
    }

    private static string NewOutDir(string testName)
    {
      return Path.Combine(Environment.CurrentDirectory, ".out", "resource-multidot-tests",
        testName + "-" + Guid.NewGuid().ToString("N"));
    }

    private static void Cleanup(string dir)
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, true);
    }

    private static void AssertFile(string dir, string relativePath, string expectedContent)
    {
      var path = Path.Combine(dir, relativePath);
      Assert.True(File.Exists(path), $"Expected file at {path}");
      Assert.Equal(expectedContent, File.ReadAllText(path));
    }
  }
}
