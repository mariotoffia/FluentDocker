using System;
using System.IO;
using System.Net.Http;
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
    public async Task BuildAsync_StreamOpenThrows_ReturnsBuildFailed()
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

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.BuildFailed, result.ErrorCode);
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
  }
}
