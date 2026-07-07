using System;
using FluentDocker.Drivers.Docker.Api.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// Unit tests for <see cref="DockerIgnoreFilter"/> (A4) — covers comments/blanks,
  /// single- and multi-segment wildcards, single-char wildcard, anchoring, directory
  /// subtree exclusion, negation/override ordering, and the always-included files.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerIgnoreFilterTests
  {
    [Fact]
    public void BlankLinesAndComments_AreIgnored()
    {
      var f = DockerIgnoreFilter.FromLines(["", "   ", "# a comment", "  # indented comment"]);

      Assert.False(f.IsIgnored("anything.txt"));
      Assert.False(f.IsIgnored("dir/file.bin"));
    }

    [Theory]
    [InlineData("./")]
    [InlineData("!")]
    public void EmptyAfterPrefixOrNegation_IsIgnored(string line)
    {
      var f = DockerIgnoreFilter.FromLines([line]);

      Assert.False(f.IsIgnored("anything.txt"));
    }

    [Fact]
    public void Star_MatchesSingleSegmentOnly()
    {
      var f = DockerIgnoreFilter.FromLines(["*.log"]);

      Assert.True(f.IsIgnored("a.log"));
      // '*' does not cross a path separator.
      Assert.False(f.IsIgnored("dir/a.log"));
      Assert.False(f.IsIgnored("a.txt"));
    }

    [Fact]
    public void DoubleStar_MatchesAcrossSegments()
    {
      var f = DockerIgnoreFilter.FromLines(["**/*.log"]);

      Assert.True(f.IsIgnored("a.log"));
      Assert.True(f.IsIgnored("dir/a.log"));
      Assert.True(f.IsIgnored("dir/sub/a.log"));
      Assert.False(f.IsIgnored("dir/a.txt"));
    }

    [Fact]
    public void QuestionMark_MatchesSingleChar()
    {
      var f = DockerIgnoreFilter.FromLines(["file?.txt"]);

      Assert.True(f.IsIgnored("file1.txt"));
      Assert.False(f.IsIgnored("file12.txt"));
      Assert.False(f.IsIgnored("file.txt"));
    }

    [Fact]
    public void LeadingSlash_IsAnchoredToContextRoot()
    {
      var f = DockerIgnoreFilter.FromLines(["/secret.txt"]);

      Assert.True(f.IsIgnored("secret.txt"));
      // A same-named file in a subdirectory is NOT anchored at root.
      Assert.False(f.IsIgnored("sub/secret.txt"));
    }

    [Fact]
    public void DirectoryPattern_ExcludesWholeSubtree()
    {
      var f = DockerIgnoreFilter.FromLines(["node_modules/"]);

      Assert.False(f.IsIgnored("node_modules"));
      Assert.True(f.IsIgnored("node_modules/lib/index.js"));
      Assert.False(f.IsIgnored("src/index.js"));
    }

    [Fact]
    public void DirectorySlashPattern_DoesNotMatchSameNamedFile()
    {
      var f = DockerIgnoreFilter.FromLines(["cache/"]);

      Assert.False(f.IsIgnored("cache"));
      Assert.True(f.IsIgnored("cache/file.txt"));
    }

    [Fact]
    public void PlainDirectoryName_ExcludesSubtree()
    {
      var f = DockerIgnoreFilter.FromLines(["build"]);

      Assert.True(f.IsIgnored("build"));
      Assert.True(f.IsIgnored("build/output.bin"));
    }

    [Fact]
    public void Negation_ReincludesPreviouslyExcluded()
    {
      var f = DockerIgnoreFilter.FromLines(["*.log", "!keep.log"]);

      Assert.True(f.IsIgnored("debug.log"));
      Assert.False(f.IsIgnored("keep.log")); // re-included by later negation
    }

    [Fact]
    public void LaterRule_OverridesEarlier_LastMatchWins()
    {
      // Reverse order: the re-include comes first, then a broad exclude overrides it.
      var f = DockerIgnoreFilter.FromLines(["!keep.log", "*.log"]);

      Assert.True(f.IsIgnored("keep.log"));
    }

    [Fact]
    public void Dockerfile_IsAlwaysIncluded_EvenWhenMatched()
    {
      var f = DockerIgnoreFilter.FromLines(["*"], dockerfileName: "Dockerfile");

      Assert.False(f.IsIgnored("Dockerfile"));
      Assert.False(f.IsIgnored(".dockerignore"));
      Assert.True(f.IsIgnored("other.txt"));
    }

    [Fact]
    public void Dockerfile_IsCaseInsensitiveOnWindows()
    {
      var f = DockerIgnoreFilter.FromLines(["*"], dockerfileName: "Dockerfile");

      Assert.Equal(OperatingSystem.IsWindows(), !f.IsIgnored("dockerfile"));
    }

    [Fact]
    public void CustomDockerfileName_IsAlwaysIncluded()
    {
      var f = DockerIgnoreFilter.FromLines(["*"], dockerfileName: "Dockerfile.prod");

      Assert.False(f.IsIgnored("Dockerfile.prod"));
      // The default name is not special when a custom one is configured.
      Assert.True(f.IsIgnored("Dockerfile"));
    }

    [Fact]
    public void NoRules_IgnoresNothing()
    {
      var f = DockerIgnoreFilter.FromLines([]);

      Assert.False(f.IsIgnored("a.txt"));
      Assert.False(f.IsIgnored("deep/nested/file.bin"));
    }

    [Fact]
    public void BackslashPaths_AreNormalized()
    {
      var f = DockerIgnoreFilter.FromLines(["node_modules/"]);

      Assert.True(f.IsIgnored("node_modules/lib/index.js"));
      Assert.Equal(OperatingSystem.IsWindows(), f.IsIgnored("node_modules\\lib\\index.js"));
    }

    [Fact]
    public void BracketClass_Range_MatchesWithinRange()
    {
      var f = DockerIgnoreFilter.FromLines(["secret[0-9].pem"]);

      Assert.True(f.IsIgnored("secret1.pem"));
      Assert.False(f.IsIgnored("secretX.pem"));
    }

    [Fact]
    public void BracketClass_Negation_ExcludesListedChars()
    {
      var f = DockerIgnoreFilter.FromLines(["img-[!0-9].png"]);

      Assert.True(f.IsIgnored("img-a.png"));
      Assert.False(f.IsIgnored("img-5.png"));
    }

    [Fact]
    public void BracketClass_CaretNegation_ExcludesListedChars()
    {
      var f = DockerIgnoreFilter.FromLines(["[^a]bc"]);

      Assert.True(f.IsIgnored("bbc"));
      Assert.False(f.IsIgnored("abc"));
    }
  }
}
