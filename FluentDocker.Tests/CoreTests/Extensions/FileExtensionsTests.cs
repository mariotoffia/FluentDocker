using System;
using System.IO;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class FileExtensionsTests : IDisposable
  {
    private readonly string _tempDir;

    public FileExtensionsTests()
    {
      _tempDir = Path.Combine(Path.GetTempPath(), "FluentDockerTests_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);

      GC.SuppressFinalize(this);
    }

    // ── EscapePath(string) ──────────────────────────────────────────────

    [Fact]
    public void EscapePath_NullString_ReturnsNull()
    {
      // Arrange
      string path = null;

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Null(result);
    }

    [Fact]
    public void EscapePath_EmptyString_ReturnsEmpty()
    {
      // Arrange
      var path = string.Empty;

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EscapePath_PathWithoutSpaces_ReturnsSamePath()
    {
      // Arrange
      var path = "/usr/local/bin/docker";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("/usr/local/bin/docker", result);
    }

    [Fact]
    public void EscapePath_PathWithSpaces_WrapsInQuotes()
    {
      // Arrange
      var path = "/usr/local/my folder/docker";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("\"/usr/local/my folder/docker\"", result);
    }

    [Fact]
    public void EscapePath_AlreadyQuoted_DoesNotDoubleQuote()
    {
      // Arrange
      var path = "\"/usr/local/my folder/docker\"";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("\"/usr/local/my folder/docker\"", result);
    }

    // ── EscapePath(TemplateString) ──────────────────────────────────────

    [Fact]
    public void EscapePathTemplate_NullOrEmpty_ReturnsSame()
    {
      // Arrange
      TemplateString path = "";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("", result.Rendered);
    }

    [Fact]
    public void EscapePathTemplate_NoSpaces_ReturnsSame()
    {
      // Arrange
      TemplateString path = "/usr/local/bin/docker";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("/usr/local/bin/docker", result.Rendered);
    }

    [Fact]
    public void EscapePathTemplate_WithSpaces_WrapsInQuotes()
    {
      // Arrange
      TemplateString path = "/usr/local/my folder/docker";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("\"/usr/local/my folder/docker\"", result.Rendered);
    }

    [Fact]
    public void EscapePathTemplate_AlreadyQuoted_DoesNotDoubleQuote()
    {
      // Arrange
      TemplateString path = "\"/usr/local/my folder/docker\"";

      // Act
      var result = path.EscapePath();

      // Assert
      Assert.Equal("\"/usr/local/my folder/docker\"", result.Rendered);
    }

    // ── ToFile ──────────────────────────────────────────────────────────

    [Fact]
    public void ToFile_WritesContentsToPath()
    {
      // Arrange
      var filePath = Path.Combine(_tempDir, "test.txt");
      var contents = "Hello, FluentDocker!";

      // Act
      contents.ToFile(filePath);

      // Assert
      Assert.True(File.Exists(filePath));
      Assert.Equal(contents, File.ReadAllText(filePath));
    }

    [Fact]
    public void ToFile_CreatesDirectoryIfMissing()
    {
      // Arrange
      var nestedDir = Path.Combine(_tempDir, "sub1", "sub2");
      var filePath = Path.Combine(nestedDir, "deep.txt");
      var contents = "nested content";

      // Act
      contents.ToFile(filePath);

      // Assert
      Assert.True(Directory.Exists(nestedDir));
      Assert.True(File.Exists(filePath));
      Assert.Equal(contents, File.ReadAllText(filePath));
    }

    // ── FromFile ────────────────────────────────────────────────────────

    [Fact]
    public void FromFile_ReadsFileWithDefaultEncoding()
    {
      // Arrange
      var filePath = Path.Combine(_tempDir, "read-default.txt");
      var expected = "default encoding content";
      File.WriteAllText(filePath, expected, Encoding.UTF8);
      TemplateString templatePath = filePath;

      // Act
      var result = templatePath.FromFile();

      // Assert
      Assert.Equal(expected, result);
    }

    [Fact]
    public void FromFile_ReadsFileWithSpecifiedEncoding()
    {
      // Arrange
      var filePath = Path.Combine(_tempDir, "read-utf8.txt");
      var expected = "UTF-8 content with special chars: ae oe aa";
      File.WriteAllText(filePath, expected, Encoding.UTF8);
      TemplateString templatePath = filePath;

      // Act
      var result = templatePath.FromFile(Encoding.UTF8);

      // Assert
      Assert.Equal(expected, result);
    }

    // ── Copy (file) ─────────────────────────────────────────────────────

    [Fact]
    public void Copy_ExistingFile_CopiesAndReturnsFileName()
    {
      // Arrange
      var sourceFile = Path.Combine(_tempDir, "source.txt");
      File.WriteAllText(sourceFile, "copy me");
      var workdir = Path.Combine(_tempDir, "workdir");
      Directory.CreateDirectory(workdir);
      TemplateString templateSource = sourceFile;
      TemplateString templateWorkdir = workdir;

      // Act
      var result = templateSource.Copy(templateWorkdir);

      // Assert
      Assert.Equal("source.txt", result);
      Assert.True(File.Exists(Path.Combine(workdir, "source.txt")));
      Assert.Equal("copy me", File.ReadAllText(Path.Combine(workdir, "source.txt")));
    }

    [Fact]
    public void Copy_NonExistentPath_ReturnsNull()
    {
      // Arrange
      var nonExistent = Path.Combine(_tempDir, "does_not_exist");
      var workdir = Path.Combine(_tempDir, "workdir");
      Directory.CreateDirectory(workdir);
      TemplateString templateSource = nonExistent;
      TemplateString templateWorkdir = workdir;

      // Act
      var result = templateSource.Copy(templateWorkdir);

      // Assert
      Assert.Null(result);
    }

    // ── Copy (directory) ────────────────────────────────────────────────

    [Fact]
    public void Copy_ExistingDirectory_CopiesRecursively()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "srcdir");
      Directory.CreateDirectory(sourceDir);
      File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "file a");
      var subDir = Path.Combine(sourceDir, "child");
      Directory.CreateDirectory(subDir);
      File.WriteAllText(Path.Combine(subDir, "b.txt"), "file b");

      var workdir = Path.Combine(_tempDir, "workdir");
      Directory.CreateDirectory(workdir);
      TemplateString templateSource = sourceDir;
      TemplateString templateWorkdir = workdir;

      // Act
      var result = templateSource.Copy(templateWorkdir);

      // Assert - Copy calls CopyTo which copies the contents of sourceDir into workdir,
      // and returns the directory name of the source.
      Assert.Equal("srcdir", result);
      Assert.True(File.Exists(Path.Combine(workdir, "a.txt")));
      Assert.Equal("file a", File.ReadAllText(Path.Combine(workdir, "a.txt")));
      Assert.True(Directory.Exists(Path.Combine(workdir, "child")));
      Assert.True(File.Exists(Path.Combine(workdir, "child", "b.txt")));
      Assert.Equal("file b", File.ReadAllText(Path.Combine(workdir, "child", "b.txt")));
    }

    // ── CopyTo ──────────────────────────────────────────────────────────

    [Fact]
    public void CopyTo_CopiesDirectoryRecursively()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "copyfrom");
      Directory.CreateDirectory(sourceDir);
      File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root content");
      var sub = Path.Combine(sourceDir, "level1");
      Directory.CreateDirectory(sub);
      File.WriteAllText(Path.Combine(sub, "nested.txt"), "nested content");

      var targetDir = Path.Combine(_tempDir, "copyto");
      TemplateString templateSource = sourceDir;
      TemplateString templateTarget = targetDir;

      // Act
      templateSource.CopyTo(templateTarget);

      // Assert
      Assert.True(Directory.Exists(targetDir));
      Assert.True(File.Exists(Path.Combine(targetDir, "root.txt")));
      Assert.Equal("root content", File.ReadAllText(Path.Combine(targetDir, "root.txt")));
      Assert.True(File.Exists(Path.Combine(targetDir, "level1", "nested.txt")));
      Assert.Equal("nested content", File.ReadAllText(Path.Combine(targetDir, "level1", "nested.txt")));
    }

    [Fact]
    public void CopyTo_CopiesSubdirectories()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "multi");
      Directory.CreateDirectory(sourceDir);
      var dirA = Path.Combine(sourceDir, "dirA");
      var dirB = Path.Combine(sourceDir, "dirB");
      Directory.CreateDirectory(dirA);
      Directory.CreateDirectory(dirB);
      File.WriteAllText(Path.Combine(dirA, "a.txt"), "a");
      File.WriteAllText(Path.Combine(dirB, "b.txt"), "b");

      var targetDir = Path.Combine(_tempDir, "multitarget");
      TemplateString templateSource = sourceDir;
      TemplateString templateTarget = targetDir;

      // Act
      templateSource.CopyTo(templateTarget);

      // Assert
      Assert.True(Directory.Exists(Path.Combine(targetDir, "dirA")));
      Assert.True(Directory.Exists(Path.Combine(targetDir, "dirB")));
      Assert.True(File.Exists(Path.Combine(targetDir, "dirA", "a.txt")));
      Assert.True(File.Exists(Path.Combine(targetDir, "dirB", "b.txt")));
      Assert.Equal("a", File.ReadAllText(Path.Combine(targetDir, "dirA", "a.txt")));
      Assert.Equal("b", File.ReadAllText(Path.Combine(targetDir, "dirB", "b.txt")));
    }
  }
}
