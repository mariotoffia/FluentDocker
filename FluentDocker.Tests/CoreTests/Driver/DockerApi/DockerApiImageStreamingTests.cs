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
  /// Tests for streaming image operations: Push, Pull (edge cases), Save, Load, Import.
  /// These operations use NDJSON streaming via PostStreamAsync/GetStreamAsync.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiImageStreamingTests
  {
    private static DriverContext Ctx => new("docker-api-stream-test");

    private static DockerApiImageDriver CreateDriver(IDockerApiConnection conn)
    {
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(new DriverContext("test"));
      return driver;
    }

    #region PushAsync

    [Fact]
    public async Task PushAsync_StreamsProgress_ReturnsSuccess()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/",
          "{\"status\":\"Pushing\",\"id\":\"layer1\"}\n"
          + "{\"status\":\"Pushed\",\"progressDetail\":{\"current\":500,\"total\":1000},\"id\":\"layer1\"}\n"
          + "{\"status\":\"latest: digest: sha256:abc123\"}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
    }

    [Fact]
    public async Task PushAsync_WithError_ReturnsPushFailedErrorCode()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/",
          "{\"status\":\"Pushing\"}\n"
          + "{\"error\":\"denied: access forbidden\",\"errorDetail\":{\"message\":\"denied\"}}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/secret:latest", null, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("denied", result.Error);
    }

    [Fact]
    public async Task PushAsync_ConnectionFailure_ReturnsFailure()
    {
      var conn = new ThrowingDockerApiConnection(
          new HttpRequestException("Connection refused"));

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("Connection refused", result.Error);
    }

    #endregion

    #region PullAsync

    [Fact]
    public async Task PullAsync_NoProgressReceived_ReturnsFailure()
    {
      var conn = new MockDockerApiConnection();
      // Empty stream: no NDJSON lines at all
      conn.SetupStream("/images/create", "");

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(Ctx, "ghost/image", "latest", null, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("no response", result.Error);
    }

    #endregion

    #region SaveAsync

    [Fact]
    public async Task SaveAsync_WritesStreamToFile_ReturnsSuccess()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/get", "fake-tar-content-bytes-here");

      var driver = CreateDriver(conn);
      var tempFile = Path.GetTempFileName();
      try
      {
        var result = await driver.SaveAsync(
            Ctx, new[] { "nginx:latest" }, tempFile, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(File.Exists(tempFile));
        var content = await File.ReadAllTextAsync(tempFile, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("fake-tar-content-bytes-here", content);
      }
      finally
      {
        File.Delete(tempFile);
      }
    }

    [Fact]
    public async Task SaveAsync_StreamThrows_ReturnsSaveFailedError()
    {
      var conn = new ThrowingDockerApiConnection(
          new HttpRequestException("daemon not reachable"),
          throwOnGetStream: true);

      var driver = CreateDriver(conn);
      var tempFile = Path.GetTempFileName();
      try
      {
        var result = await driver.SaveAsync(
            Ctx, new[] { "nginx:latest" }, tempFile, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.Image.SaveFailed, result.ErrorCode);
        Assert.Contains("daemon not reachable", result.Error);
      }
      finally
      {
        File.Delete(tempFile);
      }
    }

    #endregion

    #region LoadAsync

    [Fact]
    public async Task LoadAsync_FileNotFound_ReturnsLoadFailedError()
    {
      var conn = new MockDockerApiConnection();
      var driver = CreateDriver(conn);

      var result = await driver.LoadAsync(
          Ctx, "/tmp/nonexistent-file-12345.tar", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.LoadFailed, result.ErrorCode);
      Assert.Contains("not found", result.Error);
    }

    #endregion

    #region ImportAsync

    [Fact]
    public async Task ImportAsync_FileNotFound_ReturnsImportFailedError()
    {
      var conn = new MockDockerApiConnection();
      var driver = CreateDriver(conn);

      var result = await driver.ImportAsync(
          Ctx, "/tmp/nonexistent-import-67890.tar",
          "myrepo", "latest", null, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.ImportFailed, result.ErrorCode);
      Assert.Contains("not found", result.Error);
    }

    #endregion

    #region Helper: Throwing Mock

    /// <summary>
    /// A mock connection that throws on PostStreamAsync and/or GetStreamAsync
    /// to simulate connection failures.
    /// </summary>
    private sealed class ThrowingDockerApiConnection : IDockerApiConnection
    {
      private readonly Exception _exception;
      private readonly bool _throwOnGetStream;

      public ThrowingDockerApiConnection(
          Exception exception, bool throwOnGetStream = false)
      {
        _exception = exception;
        _throwOnGetStream = throwOnGetStream;
      }

      public string ApiVersion => "1.45";

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
          => throw new NotSupportedException();

      public Task<HttpResponseMessage> PostAsync(
          string path, HttpContent content, CancellationToken ct)
          => throw new NotSupportedException();

      public Task<HttpResponseMessage> PutAsync(
          string path, HttpContent content, CancellationToken ct)
          => throw new NotSupportedException();

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct)
          => throw new NotSupportedException();

      public Task<Stream> GetStreamAsync(string path, CancellationToken ct)
      {
        if (_throwOnGetStream)
          throw _exception;
        return Task.FromResult<Stream>(new MemoryStream());
      }

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content, CancellationToken ct)
          => throw _exception;

      public Task<bool> PingAsync(CancellationToken ct)
          => Task.FromResult(false);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
  }
}
