using System.Formats.Tar;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using SharpCompress.Common;
using SharpCompress.Writers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public partial class DockerApiContainerOperationsTests
  {
    [Fact]
    public async Task CopyFromAsync_ToFilePath_WritesExtractedFileContent()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom");
      var sourceRoot = Path.Combine(outputRoot, "source");
      var tarPath = Path.Combine(outputRoot, "archive.tar");
      var destination = Path.Combine(outputRoot, "copied.txt");
      if (Directory.Exists(outputRoot))
        Directory.Delete(outputRoot, recursive: true);
      Directory.CreateDirectory(sourceRoot);
      await File.WriteAllTextAsync(Path.Combine(sourceRoot, "source.txt"), "from api",
          TestContext.Current.CancellationToken);
      using (var tar = File.Create(tarPath))
      using (var writer = WriterFactory.OpenWriter(tar, ArchiveType.Tar,
          new WriterOptions(CompressionType.None)))
      {
        writer.Write("source.txt", Path.Combine(sourceRoot, "source.txt"));
      }

      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", await File.ReadAllBytesAsync(
          tarPath, TestContext.Current.CancellationToken));
      var driver = CreateDriver(mock);

      var result = await driver.CopyFromAsync(Ctx, "ctr1", "/tmp/source.txt", destination,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("from api", await File.ReadAllTextAsync(
          destination, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyFromAsync_RejectsArchivePathTraversal()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-traversal");
      var tarPath = Path.Combine(outputRoot, "archive.tar");
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      var escaped = Path.Combine(outputRoot, "escape.txt");
      if (Directory.Exists(outputRoot))
        Directory.Delete(outputRoot, recursive: true);
      Directory.CreateDirectory(outputRoot);
      await using (var tar = File.Create(tarPath))
      await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: false))
      {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escape.txt")
        {
          DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("escape"))
        };
        await writer.WriteEntryAsync(entry, TestContext.Current.CancellationToken);
      }

      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", await File.ReadAllBytesAsync(
          tarPath, TestContext.Current.CancellationToken));
      var driver = CreateDriver(mock);

      var result = await driver.CopyFromAsync(Ctx, "ctr1", "/tmp/escape.txt", destination,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.False(File.Exists(escaped));
    }

    [Fact]
    public async Task CopyFromAsync_ToFilePath_ReportsArchiveWithMultipleFiles()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-multiple");
      var tarPath = Path.Combine(outputRoot, "archive.tar");
      var destination = Path.Combine(outputRoot, "copied.txt");
      if (Directory.Exists(outputRoot))
        Directory.Delete(outputRoot, recursive: true);
      Directory.CreateDirectory(outputRoot);
      await using (var tar = File.Create(tarPath))
      await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: false))
      {
        await writer.WriteEntryAsync(CreateEntry("one.txt"), TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(CreateEntry("two.txt"), TestContext.Current.CancellationToken);
      }

      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", await File.ReadAllBytesAsync(
          tarPath, TestContext.Current.CancellationToken));
      var driver = CreateDriver(mock);

      var result = await driver.CopyFromAsync(Ctx, "ctr1", "/tmp/files", destination,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("contained 2 files", result.Error);
    }

    private static PaxTarEntry CreateEntry(string name) =>
        new(TarEntryType.RegularFile, name)
        {
          DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(name))
        };

  }
}
