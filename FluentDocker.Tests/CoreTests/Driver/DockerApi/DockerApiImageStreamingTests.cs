using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
  public partial class DockerApiImageStreamingTests
  {
    private static DriverContext Ctx => new("docker-api-stream-test");

    private static DockerApiImageDriver CreateDriver(IDockerApiConnection conn)
    {
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(new DriverContext("test"));
      return driver;
    }

    [Fact]
    public async Task PullAsync_AfterLogin_SendsRegistryAuthHeader()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/auth", 200, "{}");
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");
      var auth = new DockerApiAuthDriver(conn);
      var driver = CreateDriver(conn);

      var login = await auth.LoginAsync(Ctx, new RegistryLoginConfig
      {
        Server = "registry.example.com",
        Username = "me",
        Password = "secret",
        Email = "me@example.com"
      }, TestContext.Current.CancellationToken);
      var result = await driver.PullAsync(Ctx, "registry.example.com/team/app", "latest",
          null!, TestContext.Current.CancellationToken);

      Assert.True(login.Success);
      Assert.True(result.Success);
      var request = conn.GetRequests().Last(r => r.Method == "POST_STREAM");
      Assert.NotNull(request.Headers);
      Assert.True(request.Headers!.TryGetValue("X-Registry-Auth", out var value));
      var expected = ExpectedRegistryAuthHeader(new
      {
        username = "me",
        password = "secret",
        email = "me@example.com",
        serveraddress = "registry.example.com"
      });
      Assert.EndsWith("=", expected);
      Assert.Equal(expected, value);
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
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
    }

    [Fact]
    public async Task PushAsync_AfterLogin_SendsRegistryAuthHeader()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/auth", 200, "{}");
      conn.SetupStream("/images/", "{\"status\":\"Pushing\"}\n");
      var auth = new DockerApiAuthDriver(conn);
      var driver = CreateDriver(conn);

      await auth.LoginAsync(Ctx, new RegistryLoginConfig
      {
        Server = "registry.example.com",
        Username = "me",
        Password = "secret"
      }, TestContext.Current.CancellationToken);
      var result = await driver.PushAsync(Ctx, "registry.example.com/team/app:latest",
          null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = conn.GetRequests().Last(r => r.Method == "POST_STREAM");
      Assert.NotNull(request.Headers);
      Assert.Equal(ExpectedRegistryAuthHeader(new
      {
        username = "me",
        password = "secret",
        serveraddress = "registry.example.com"
      }), request.Headers!["X-Registry-Auth"]);
    }

    private static string ExpectedRegistryAuthHeader<T>(T value)
    {
      return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonHelper.Serialize(value)))
          .Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task PushAsync_WithError_ReturnsPushFailedErrorCode()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/",
          "{\"status\":\"Pushing\"}\n"
          + "{\"error\":\"denied: access forbidden\",\"errorDetail\":{\"message\":\"denied\"}}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/secret:latest", null!, TestContext.Current.CancellationToken);

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
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("Connection refused", result.Error);
    }

    [Fact]
    public async Task PushAsync_StreamThrowsMidRead_ReturnsPushFailed()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStreamReadThrows("/images/",
          System.Text.Encoding.UTF8.GetBytes("{\"status\":\"Pushing\"}\n"),
          new IOException("connection reset by peer"));

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("connection reset by peer", result.Error);
    }

    [Fact]
    public async Task PushAsync_EmptyStream_ReturnsPushFailedNoEvidence()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/", "");

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "myrepo/myimage:latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("no response", result.Error);
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
      var result = await driver.PullAsync(Ctx, "ghost/image", "latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("no response", result.Error);
    }

    [Fact]
    public async Task PullAsync_StreamThrowsMidRead_ReturnsPullFailed()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStreamReadThrows("/images/create",
          System.Text.Encoding.UTF8.GetBytes("{\"status\":\"Pulling from lib\"}\n"),
          new IOException("connection reset by peer"));

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(Ctx, "lib/image", "latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("connection reset by peer", result.Error);
    }

    [Fact]
    public async Task PullAsync_ConflictingDigests_ReturnsPullFailed()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"ok\"}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(
          Ctx, "repo@sha256:aaa", "sha256:bbb", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("Conflicting digests", result.Error);
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
            Ctx, ["nginx:latest"], tempFile, TestContext.Current.CancellationToken);

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
            Ctx, ["nginx:latest"], tempFile, TestContext.Current.CancellationToken);

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
          "myrepo", "latest", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.ImportFailed, result.ErrorCode);
      Assert.Contains("not found", result.Error);
    }

    #endregion

    #region A3 - Pull disposes the response stream

    [Fact]
    public async Task PullAsync_DisposesResponseStream()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PullAsync(Ctx, "repo", "latest", null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Single(conn.Streams);
      Assert.True(conn.Streams[0].IsDisposed);
    }

    [Fact]
    public async Task PushAsync_DisposesResponseStream()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/", "{\"status\":\"latest: digest: sha256:abc\"}\n");

      var driver = CreateDriver(conn);
      var result = await driver.PushAsync(Ctx, "repo/img:latest", null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Single(conn.Streams);
      Assert.True(conn.Streams[0].IsDisposed);
    }

    #endregion

    #region M6 - Digest pull reference handling

    [Fact]
    public async Task PullAsync_PlainRepoNullTag_AppendsLatestTag()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(Ctx, "repo", null!, null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains("tag=latest", req.Path);
      Assert.Contains("fromImage=repo", req.Path);
    }

    [Fact]
    public async Task PullAsync_ExplicitTag_UsesThatTag()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(Ctx, "repo", "1.2", null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.Contains("tag=1.2", req.Path);
      Assert.DoesNotContain("tag=latest", req.Path);
    }

    [Fact]
    public async Task PullAsync_DigestReference_OmitsTagAndCarriesDigest()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/create", "{\"status\":\"Pulling\"}\n");

      var driver = CreateDriver(conn);
      await driver.PullAsync(Ctx, "repo@sha256:abc123", null!, null!, TestContext.Current.CancellationToken);

      var req = FindStreamRequest(conn, "/images/create");
      Assert.DoesNotContain("&tag=", req.Path);
      Assert.Contains("sha256", req.Path);
    }

    private static CapturedRequest FindStreamRequest(
        MockDockerApiConnection conn, string pathContains)
    {
      foreach (var r in conn.GetRequests())
        if (r.Method == "POST_STREAM" && r.Path.Contains(pathContains))
          return r;
      throw new Xunit.Sdk.XunitException($"No POST_STREAM request matched '{pathContains}'");
    }

    #endregion

    #region Helper: Throwing Mock

    /// <summary>
    /// A mock connection that throws on PostStreamAsync and/or GetStreamAsync
    /// to simulate connection failures.
    /// </summary>
    private sealed class ThrowingDockerApiConnection(
        Exception exception, bool throwOnGetStream = false) : IDockerApiConnection
    {
      private readonly Exception _exception = exception;
      private readonly bool _throwOnGetStream = throwOnGetStream;

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

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content,
          System.Collections.Generic.IReadOnlyDictionary<string, string> headers, CancellationToken ct)
          => throw _exception;

      public Task<bool> PingAsync(CancellationToken ct)
          => Task.FromResult(false);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
  }
}
