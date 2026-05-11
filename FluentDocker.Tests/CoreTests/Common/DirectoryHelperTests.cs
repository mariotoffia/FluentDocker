using System;
using System.IO;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class DirectoryHelperTests : IDisposable
  {
    private readonly string _tempDir;

    public DirectoryHelperTests()
    {
      _tempDir = Path.Combine(Path.GetTempPath(), "FluentDockerDirTests_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);
    }

    #region CopyFilesRecursively

    [Fact]
    public void CopyFilesRecursively_CopiesFilesAndDirs()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "source");
      var targetDir = Path.Combine(_tempDir, "target");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(targetDir);

      File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "content1");
      File.WriteAllText(Path.Combine(sourceDir, "file2.txt"), "content2");

      var subDir = Path.Combine(sourceDir, "subdir");
      Directory.CreateDirectory(subDir);
      File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested-content");

      // Act
      DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(sourceDir), new DirectoryInfo(targetDir));

      // Assert
      Assert.True(File.Exists(Path.Combine(targetDir, "file1.txt")));
      Assert.True(File.Exists(Path.Combine(targetDir, "file2.txt")));
      Assert.True(Directory.Exists(Path.Combine(targetDir, "subdir")));
      Assert.True(File.Exists(Path.Combine(targetDir, "subdir", "nested.txt")));

      Assert.Equal("content1", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
      Assert.Equal("content2", File.ReadAllText(Path.Combine(targetDir, "file2.txt")));
      Assert.Equal("nested-content", File.ReadAllText(Path.Combine(targetDir, "subdir", "nested.txt")));
    }

    [Fact]
    public void CopyFilesRecursively_RenamesDotGit()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "source");
      var targetDir = Path.Combine(_tempDir, "target");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(targetDir);

      var dotGitDir = Path.Combine(sourceDir, "dot_git");
      Directory.CreateDirectory(dotGitDir);
      File.WriteAllText(Path.Combine(dotGitDir, "HEAD"), "ref: refs/heads/main");

      // Act
      DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(sourceDir), new DirectoryInfo(targetDir));

      // Assert
      Assert.False(Directory.Exists(Path.Combine(targetDir, "dot_git")));
      Assert.True(Directory.Exists(Path.Combine(targetDir, ".git")));
      Assert.True(File.Exists(Path.Combine(targetDir, ".git", "HEAD")));
      Assert.Equal("ref: refs/heads/main", File.ReadAllText(Path.Combine(targetDir, ".git", "HEAD")));
    }

    [Fact]
    public void CopyFilesRecursively_RenamesGitmodules()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "source");
      var targetDir = Path.Combine(_tempDir, "target");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(targetDir);

      File.WriteAllText(Path.Combine(sourceDir, "gitmodules"), "[submodule \"lib\"]");

      // Act
      DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(sourceDir), new DirectoryInfo(targetDir));

      // Assert
      Assert.False(File.Exists(Path.Combine(targetDir, "gitmodules")));
      Assert.True(File.Exists(Path.Combine(targetDir, ".gitmodules")));
      Assert.Equal("[submodule \"lib\"]", File.ReadAllText(Path.Combine(targetDir, ".gitmodules")));
    }

    [Fact]
    public void CopyFilesRecursively_LeavesNonSpecialNames()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "source");
      var targetDir = Path.Combine(_tempDir, "target");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(targetDir);

      File.WriteAllText(Path.Combine(sourceDir, "readme.md"), "# Hello");
      var regularSubDir = Path.Combine(sourceDir, "docs");
      Directory.CreateDirectory(regularSubDir);
      File.WriteAllText(Path.Combine(regularSubDir, "guide.txt"), "guide content");

      // Act
      DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(sourceDir), new DirectoryInfo(targetDir));

      // Assert
      Assert.True(File.Exists(Path.Combine(targetDir, "readme.md")));
      Assert.True(Directory.Exists(Path.Combine(targetDir, "docs")));
      Assert.True(File.Exists(Path.Combine(targetDir, "docs", "guide.txt")));
      Assert.Equal("# Hello", File.ReadAllText(Path.Combine(targetDir, "readme.md")));
      Assert.Equal("guide content", File.ReadAllText(Path.Combine(targetDir, "docs", "guide.txt")));
    }

    [Fact]
    public void CopyFilesRecursively_EmptySourceDir()
    {
      // Arrange
      var sourceDir = Path.Combine(_tempDir, "source");
      var targetDir = Path.Combine(_tempDir, "target");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(targetDir);

      // Act
      DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(sourceDir), new DirectoryInfo(targetDir));

      // Assert
      Assert.True(Directory.Exists(targetDir));
      Assert.Empty(Directory.GetFiles(targetDir));
      Assert.Empty(Directory.GetDirectories(targetDir));
    }

    #endregion

    #region DeleteDirectory

    [Fact]
    public void DeleteDirectory_NonExistentPath_DoesNotThrow()
    {
      // Arrange
      var nonExistent = Path.Combine(_tempDir, "does_not_exist");

      // Act & Assert - should not throw
      var ex = Record.Exception(() => DirectoryHelper.DeleteDirectory(nonExistent));
      Assert.Null(ex);
    }

    [Fact]
    public void DeleteDirectory_ExistingEmptyDir_DeletesIt()
    {
      // Arrange
      var dirToDelete = Path.Combine(_tempDir, "empty_dir");
      Directory.CreateDirectory(dirToDelete);
      Assert.True(Directory.Exists(dirToDelete));

      // Act
      DirectoryHelper.DeleteDirectory(dirToDelete);

      // Assert
      Assert.False(Directory.Exists(dirToDelete));
    }

    [Fact]
    public void DeleteDirectory_ExistingDirWithFiles_DeletesAll()
    {
      // Arrange
      var dirToDelete = Path.Combine(_tempDir, "dir_with_files");
      Directory.CreateDirectory(dirToDelete);
      File.WriteAllText(Path.Combine(dirToDelete, "a.txt"), "aaa");
      File.WriteAllText(Path.Combine(dirToDelete, "b.txt"), "bbb");

      var subDir = Path.Combine(dirToDelete, "sub");
      Directory.CreateDirectory(subDir);
      File.WriteAllText(Path.Combine(subDir, "c.txt"), "ccc");

      Assert.True(Directory.Exists(dirToDelete));

      // Act
      DirectoryHelper.DeleteDirectory(dirToDelete);

      // Assert
      Assert.False(Directory.Exists(dirToDelete));
    }

    [Fact]
    public void DeleteDirectory_ReadOnlyFiles_NormalizesAndDeletes()
    {
      // Arrange
      var dirToDelete = Path.Combine(_tempDir, "readonly_dir");
      Directory.CreateDirectory(dirToDelete);

      var readOnlyFile = Path.Combine(dirToDelete, "locked.txt");
      File.WriteAllText(readOnlyFile, "locked content");
      File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

      Assert.True(Directory.Exists(dirToDelete));
      Assert.True(File.GetAttributes(readOnlyFile).HasFlag(FileAttributes.ReadOnly));

      // Act
      DirectoryHelper.DeleteDirectory(dirToDelete);

      // Assert
      Assert.False(Directory.Exists(dirToDelete));
    }

    #endregion

    #region GetTempPath

    [Fact]
    public void GetTempPath_Default_ReturnsTempPath()
    {
      // Act
      var result = DirectoryHelper.GetTempPath();

      // Assert
      Assert.NotNull(result);
      Assert.NotEmpty(result);
    }

    [Fact]
    public void GetTempPath_CanBeOverridden()
    {
      // Arrange
      var original = DirectoryHelper.GetTempPath;

      try
      {
        const string customPath = "/custom/temp/path";
        DirectoryHelper.GetTempPath = () => customPath;

        // Act
        var result = DirectoryHelper.GetTempPath();

        // Assert
        Assert.Equal(customPath, result);
      }
      finally
      {
        DirectoryHelper.GetTempPath = original;
      }
    }

    [Fact]
    public void GetTempPath_Override_RestoresOriginal()
    {
      // Arrange
      var original = DirectoryHelper.GetTempPath;
      var originalResult = original();

      try
      {
        // Override with a custom value
        DirectoryHelper.GetTempPath = () => "/overridden/path";
        Assert.Equal("/overridden/path", DirectoryHelper.GetTempPath());

        // Act - restore original
        DirectoryHelper.GetTempPath = original;
        var restoredResult = DirectoryHelper.GetTempPath();

        // Assert
        Assert.Equal(originalResult, restoredResult);
      }
      finally
      {
        // Safety net: always restore
        DirectoryHelper.GetTempPath = original;
      }
    }

    #endregion
  }
}
