using System;
using System.IO;
using System.Text;
using FluentDocker.Extensions;
using SharpCompress.Common;
using SharpCompress.Writers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Extensions
{
  [Trait("Category", "Unit")]
  public class CompressionExtensionsTests : IDisposable
  {
    private readonly string _tempDir;

    public CompressionExtensionsTests()
    {
      _tempDir = Path.Combine(
        Path.GetTempPath(),
        "FluentDockerTarTests_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);
    }

    private string CreateTarWithFile(string fileName, string content)
    {
      var tarPath = Path.Combine(_tempDir, "test.tar");
      using var fileStream = File.Create(tarPath);
      using var writer = WriterFactory.Open(
        fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.None));

      using var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
      writer.Write(fileName, contentStream);

      return tarPath;
    }

    private string CreateTarWithFiles(params (string fileName, string content)[] entries)
    {
      var tarPath = Path.Combine(_tempDir, "test.tar");
      using var fileStream = File.Create(tarPath);
      using var writer = WriterFactory.Open(
        fileStream, ArchiveType.Tar, new WriterOptions(CompressionType.None));

      foreach (var (fileName, content) in entries)
      {
        using var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        writer.Write(fileName, contentStream);
      }

      return tarPath;
    }

    [Fact]
    public void UnTar_ValidTarFile_ExtractsFiles()
    {
      // Arrange
      var tarPath = CreateTarWithFile("hello.txt", "Hello, World!");
      var destPath = Path.Combine(_tempDir, "output");
      Directory.CreateDirectory(destPath);

      // Act
      tarPath.UnTar(destPath);

      // Assert
      var extractedFile = Path.Combine(destPath, "hello.txt");
      Assert.True(File.Exists(extractedFile), "Extracted file should exist");
      Assert.Equal("Hello, World!", File.ReadAllText(extractedFile));
    }

    [Fact]
    public void UnTar_TarWithMultipleFiles_ExtractsAll()
    {
      // Arrange
      var tarPath = CreateTarWithFiles(
        ("file1.txt", "Content of file 1"),
        ("file2.txt", "Content of file 2"),
        ("subdir/file3.txt", "Content of file 3"));

      var destPath = Path.Combine(_tempDir, "output");
      Directory.CreateDirectory(destPath);

      // Act
      tarPath.UnTar(destPath);

      // Assert
      var file1 = Path.Combine(destPath, "file1.txt");
      var file2 = Path.Combine(destPath, "file2.txt");
      var file3 = Path.Combine(destPath, "subdir", "file3.txt");

      Assert.True(File.Exists(file1), "file1.txt should exist");
      Assert.True(File.Exists(file2), "file2.txt should exist");
      Assert.True(File.Exists(file3), "subdir/file3.txt should exist");

      Assert.Equal("Content of file 1", File.ReadAllText(file1));
      Assert.Equal("Content of file 2", File.ReadAllText(file2));
      Assert.Equal("Content of file 3", File.ReadAllText(file3));
    }

    [Fact]
    public void UnTar_NonExistentFile_ThrowsFileNotFoundException()
    {
      // Arrange
      var fakePath = Path.Combine(_tempDir, "does_not_exist.tar");
      var destPath = Path.Combine(_tempDir, "output");
      Directory.CreateDirectory(destPath);

      // Act & Assert
      Assert.Throws<FileNotFoundException>(() => fakePath.UnTar(destPath));
    }

    [Fact]
    public void UnTar_ExtractsToSpecifiedPath()
    {
      // Arrange
      var tarPath = CreateTarWithFile("data.txt", "target directory test");
      var destPath1 = Path.Combine(_tempDir, "dest1");
      var destPath2 = Path.Combine(_tempDir, "dest2");
      Directory.CreateDirectory(destPath1);
      Directory.CreateDirectory(destPath2);

      // Act
      tarPath.UnTar(destPath1);

      // Assert - file is in dest1, not in dest2
      Assert.True(
        File.Exists(Path.Combine(destPath1, "data.txt")),
        "File should exist in dest1");
      Assert.False(
        File.Exists(Path.Combine(destPath2, "data.txt")),
        "File should not exist in dest2");
    }

    [Fact]
    public void UnTar_OverwritesExistingFiles()
    {
      // Arrange
      var destPath = Path.Combine(_tempDir, "output");
      Directory.CreateDirectory(destPath);

      var existingFile = Path.Combine(destPath, "overwrite.txt");
      File.WriteAllText(existingFile, "original content");

      var tarPath = CreateTarWithFile("overwrite.txt", "new content from tar");

      // Act
      tarPath.UnTar(destPath);

      // Assert
      Assert.Equal("new content from tar", File.ReadAllText(existingFile));
    }
  }
}
