using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// Stream-open failure and missing-success-evidence tests for Build/Load/Import
  /// NDJSON image operations. Partial of <see cref="DockerApiImageStreamingTests"/>.
  /// </summary>
  public partial class DockerApiImageStreamingTests
  {
    #region A2 - Stream-open failure and missing success evidence surface as Fail

    [Fact]
    public async Task BuildAsync_StreamOpenTransportFailure_ReturnsConnectionFailed()
    {
      var dir = Directory.CreateDirectory(
          Path.Combine(Path.GetTempPath(), "fd-build-" + Guid.NewGuid().ToString("N")));
      try
      {
        File.WriteAllText(Path.Combine(dir.FullName, "Dockerfile"), "FROM scratch\n");
        var conn = new MockDockerApiConnection();
        conn.SetupStreamThrows("/build", new HttpRequestException("daemon gone"));

        var driver = CreateDriver(conn);
        var config = new ImageBuildConfig { BuildContext = dir.FullName };
        var result = await driver.BuildAsync(Ctx, config, null!, TestContext.Current.CancellationToken);

        // A daemon-down transport failure is transient: it must surface as ConnectionFailed
        // (per the BuildAsync contract) so callers can retry, not opaque BuildFailed.
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
        Assert.Contains("daemon gone", result.Error);
      }
      finally
      {
        dir.Delete(true);
      }
    }

    [Fact]
    public async Task BuildAsync_NoImageId_ReturnsBuildFailed()
    {
      var dir = Directory.CreateDirectory(
          Path.Combine(Path.GetTempPath(), "fd-build-" + Guid.NewGuid().ToString("N")));
      try
      {
        File.WriteAllText(Path.Combine(dir.FullName, "Dockerfile"), "FROM scratch\n");
        var conn = new MockDockerApiConnection();
        // Stream completes with output but no aux.ID and no error => incomplete/streamed failure.
        conn.SetupStream("/build", "{\"stream\":\"Step 1/1 : FROM scratch\"}\n");

        var driver = CreateDriver(conn);
        var config = new ImageBuildConfig { BuildContext = dir.FullName };
        var result = await driver.BuildAsync(Ctx, config, null!, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.BuildFailed, result.ErrorCode);
        Assert.Contains("no image id", result.Error);
      }
      finally
      {
        dir.Delete(true);
      }
    }

    [Fact]
    public async Task BuildAsync_WithAuxId_ReturnsSuccess()
    {
      var dir = Directory.CreateDirectory(
          Path.Combine(Path.GetTempPath(), "fd-build-" + Guid.NewGuid().ToString("N")));
      try
      {
        File.WriteAllText(Path.Combine(dir.FullName, "Dockerfile"), "FROM scratch\n");
        var conn = new MockDockerApiConnection();
        conn.SetupStream("/build",
            "{\"stream\":\"Step 1/1 : FROM scratch\"}\n"
            + "{\"aux\":{\"ID\":\"sha256:deadbeef\"}}\n");

        var driver = CreateDriver(conn);
        var config = new ImageBuildConfig { BuildContext = dir.FullName };
        var result = await driver.BuildAsync(Ctx, config, null!, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("sha256:deadbeef", result.Data.ImageId);
      }
      finally
      {
        dir.Delete(true);
      }
    }

    [Fact]
    public async Task BuildAsync_LongFileName_WritesGnuLongLinkEntry()
    {
      var dir = CreateBuildContextDirectory();
      try
      {
        File.WriteAllText(Path.Combine(dir.FullName, "Dockerfile"), "FROM scratch\n");
        var longName = new string('a', 150);
        File.WriteAllText(Path.Combine(dir.FullName, longName), "content");
        var conn = new MockDockerApiConnection();
        conn.SetupStream("/build", "{\"aux\":{\"ID\":\"sha256:deadbeef\"}}\n");

        var driver = CreateDriver(conn);
        var result = await driver.BuildAsync(
            Ctx, new ImageBuildConfig { BuildContext = dir.FullName }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var tar = conn.GetRequests().Last(r => r.Method == "POST_STREAM").BodyBytes!;
        var longLinkOffset = FindTarHeader(tar, "././@LongLink");
        Assert.True(longLinkOffset >= 0);
        Assert.Equal('L', (char)tar[longLinkOffset + 156]);
        Assert.Equal(longName, ReadTarPayloadString(tar, longLinkOffset + 512, longName.Length));
      }
      finally
      {
        dir.Delete(true);
      }
    }

    [Fact]
    public async Task BuildAsync_TarCreationFailure_ReturnsBuildFailed()
    {
      var dir = CreateBuildContextDirectory();
      try
      {
        File.WriteAllText(Path.Combine(dir.FullName, "Dockerfile"), "FROM scratch\n");
        var sparse = Path.Combine(dir.FullName, "huge.bin");
        using (var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.Write))
          stream.SetLength(9L * 1024 * 1024 * 1024);
        var conn = new MockDockerApiConnection();
        conn.SetupStream("/build", "{\"aux\":{\"ID\":\"sha256:deadbeef\"}}\n");

        var driver = CreateDriver(conn);
        var result = await driver.BuildAsync(
            Ctx, new ImageBuildConfig { BuildContext = dir.FullName }, null!,
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.BuildFailed, result.ErrorCode);
        Assert.Contains("tar", result.Error, StringComparison.OrdinalIgnoreCase);
      }
      finally
      {
        dir.Delete(true);
      }
    }

    [Fact]
    public async Task LoadAsync_NoLoadedImages_ReturnsLoadFailed()
    {
      var file = Path.GetTempFileName();
      try
      {
        await File.WriteAllTextAsync(file, "fake-tar", TestContext.Current.CancellationToken);
        var conn = new MockDockerApiConnection();
        // Stream completes without any "Loaded image" line and no error.
        conn.SetupStream("/images/load", "{\"stream\":\"Importing\"}\n");

        var driver = CreateDriver(conn);
        var result = await driver.LoadAsync(Ctx, file, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.LoadFailed, result.ErrorCode);
        Assert.Contains("no loaded images", result.Error);
      }
      finally
      {
        File.Delete(file);
      }
    }

    [Fact]
    public async Task LoadAsync_StreamOpenThrows_ReturnsLoadFailed()
    {
      var file = Path.GetTempFileName();
      try
      {
        await File.WriteAllTextAsync(file, "fake-tar", TestContext.Current.CancellationToken);
        var conn = new MockDockerApiConnection();
        conn.SetupStreamThrows("/images/load", new HttpRequestException("load boom"));

        var driver = CreateDriver(conn);
        var result = await driver.LoadAsync(Ctx, file, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.LoadFailed, result.ErrorCode);
        Assert.Contains("load boom", result.Error);
      }
      finally
      {
        File.Delete(file);
      }
    }

    [Fact]
    public async Task LoadAsync_WithLoadedImageLine_ReturnsSuccess()
    {
      var file = Path.GetTempFileName();
      try
      {
        await File.WriteAllTextAsync(file, "fake-tar", TestContext.Current.CancellationToken);
        var conn = new MockDockerApiConnection();
        conn.SetupStream("/images/load", "{\"stream\":\"Loaded image: nginx:latest\"}\n");

        var driver = CreateDriver(conn);
        var result = await driver.LoadAsync(Ctx, file, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("nginx:latest", result.Data);
      }
      finally
      {
        File.Delete(file);
      }
    }

    [Fact]
    public async Task ImportAsync_NoImageId_ReturnsImportFailed()
    {
      var file = Path.GetTempFileName();
      try
      {
        await File.WriteAllTextAsync(file, "fake-tar", TestContext.Current.CancellationToken);
        var conn = new MockDockerApiConnection();
        // No "status" line carrying the new id and no error.
        conn.SetupStream("/images/create", "{\"progress\":\"...\"}\n");

        var driver = CreateDriver(conn);
        var result = await driver.ImportAsync(
            Ctx, file, "repo", "latest", null!, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.ImportFailed, result.ErrorCode);
        Assert.Contains("no image id", result.Error);
      }
      finally
      {
        File.Delete(file);
      }
    }

    [Fact]
    public async Task ImportAsync_WithStatusId_ReturnsSuccess()
    {
      var file = Path.GetTempFileName();
      try
      {
        await File.WriteAllTextAsync(file, "fake-tar", TestContext.Current.CancellationToken);
        var conn = new MockDockerApiConnection();
        conn.SetupStream("/images/create", "{\"status\":\"sha256:imported123\"}\n");

        var driver = CreateDriver(conn);
        var result = await driver.ImportAsync(
            Ctx, file, "repo", "latest", null!, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("sha256:imported123", result.Data);
      }
      finally
      {
        File.Delete(file);
      }
    }

    #endregion

    private static DirectoryInfo CreateBuildContextDirectory()
    {
      var path = Path.Combine(
          Directory.GetCurrentDirectory(), ".out", "docker-api-build",
          Guid.NewGuid().ToString("N"));
      return Directory.CreateDirectory(path);
    }

    private static string ReadTarName(byte[] tar, int headerOffset)
    {
      var length = Array.IndexOf(tar, (byte)0, headerOffset, 100);
      return Encoding.UTF8.GetString(tar, headerOffset, length - headerOffset);
    }

    private static string ReadTarPayloadString(byte[] tar, int payloadOffset, int length) =>
        Encoding.UTF8.GetString(tar, payloadOffset, length);

    private static int FindTarHeader(byte[] tar, string name)
    {
      for (var offset = 0; offset + 512 <= tar.Length; offset += 512)
        if (ReadTarName(tar, offset) == name)
          return offset;
      return -1;
    }
  }
}
