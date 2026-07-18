using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiChunk6RemediationTests
  {
    private static DriverContext Ctx => new("docker-api-ch6-test");

    [Fact]
    public async Task TransportFailure_MapsToConnectionFailed_NotServerError()
    {
      var driver = new DockerApiContainerDriver(new ThrowingConnection(new HttpRequestException("refused")));
      driver.Initialize(Ctx);

      var result = await driver.TopAsync(Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task RequestTimeout_MapsToGeneralTimeout()
    {
      var driver = new DockerApiContainerDriver(new ThrowingConnection(new TaskCanceledException("slow")));
      driver.Initialize(new DriverContext("docker-api-ch6-test") { RequestTimeout = TimeSpan.FromSeconds(7) });

      var result = await driver.TopAsync(Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.Timeout, result.ErrorCode);
      Assert.Contains("timed out after 00:00:07", result.Error);
    }

    [Fact]
    public async Task ApiResultOk_PreservesNotModifiedStatusCode()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupDelete("/images/unused", 304, "");
      var driver = new ExposedDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.DeleteForTestAsync("/images/unused", TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(304, result.StatusCode);
    }

    [Fact]
    public async Task SaveAsync_CallerCancellation_ThrowsOperationCanceledException()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/images/get", "tar");
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(Ctx);
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.SaveAsync(Ctx, ["alpine"], ".out/ch6-cancel-save.tar", cts.Token));
    }

    [Fact]
    public async Task GetLogsAsync_TruncatesToDefaultTail()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/logs", new string('a', CliOutputTruncation.DefaultTailChars + 10));
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.StartsWith(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars), result.Data);
      Assert.EndsWith(new string('a', CliOutputTruncation.DefaultTailChars), result.Data);
    }

    [Fact]
    public async Task ServiceGetLogsAsync_Follow_ReturnsFailure()
    {
      var driver = new DockerApiServiceDriver(new MockDockerApiConnection());
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "svc",
          new ServiceLogsConfig { Follow = true }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Service.LogsFailed, result.ErrorCode);
    }

    [Fact]
    public void StreamStatsAsync_NullContainerId_ThrowsAtCallSite()
    {
      var driver = new DockerApiStreamDriver(new MockDockerApiConnection());
      driver.Initialize(Ctx);

      Assert.Throws<ArgumentException>(() => driver.StreamStatsAsync(Ctx, null, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StreamLogsAsync_PreservesEmptyRawLogLines()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupGet("/containers/raw/json", 200, "{\"Config\":{\"Tty\":true}}");
      conn.SetupStream("/containers/raw/logs", "a\n\nb\n");
      var driver = new DockerApiStreamDriver(conn);
      driver.Initialize(Ctx);
      var lines = new List<string>();

      await foreach (var line in driver.StreamLogsAsync(Ctx, "raw",
          cancellationToken: TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Equal(["a", "", "b"], lines);
    }

    [Fact]
    public async Task ExecAsync_NullConfig_ReturnsExecFailed()
    {
      var driver = new DockerApiContainerDriver(new MockDockerApiConnection());
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr", null!, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ExecAsync_RunningWithUnknownExitCode_ReturnsExecFailed()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/exec", 201, @"{""Id"":""exec-running""}");
      conn.SetupStreamBytes("/exec/exec-running/start", []);
      conn.SetupGet("/exec/exec-running/json", 200, @"{""Running"":true}");
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(Ctx);

      // The exec never reports an exit code (Running stays true), so the attached-exec poll
      // must give up at the caller deadline. Bound it with a short RequestTimeout instead of
      // the 5-minute default so the test asserts the give-up path without spinning for minutes.
      var ctx = new DriverContext("docker-api-ch6-test") { RequestTimeout = TimeSpan.FromMilliseconds(200) };
      var result = await driver.ExecAsync(ctx, "ctr",
          new ExecConfig { Command = ["true"] }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ExecAsync_Detached_ReturnsSuccessEvenWhileRunning()
    {
      // A detached exec is fire-and-forget: the process is still Running at inspect, so the
      // driver must return success (exit 0), not "exit code not available yet".
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/exec", 201, @"{""Id"":""exec-detached""}");
      conn.SetupStreamBytes("/exec/exec-detached/start", []);
      conn.SetupGet("/exec/exec-detached/json", 200, @"{""Running"":true}");
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["true"], Detach = true }, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal(0, result.Data.ExitCode);
    }

    [Fact]
    public async Task ServiceCreate_TransportFailure_MapsToConnectionFailed()
    {
      var driver = new DockerApiServiceDriver(new ThrowingConnection(new HttpRequestException("refused")));
      driver.Initialize(Ctx);

      var result = await driver.CreateAsync(Ctx,
          new ServiceCreateConfig { Name = "svc", Image = "nginx:latest" },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task DockerHubTaggedImage_UsesDockerHubCredentials()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/auth", 200, "{}");
      conn.SetupStream("/images/create", "{\"status\":\"Status: Downloaded newer image for app:v1\"}\n");
      var auth = new DockerApiAuthDriver(conn);
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(Ctx);

      await auth.LoginAsync(Ctx, new RegistryLoginConfig
      {
        Server = "docker.io",
        Username = "me",
        Password = "secret"
      }, TestContext.Current.CancellationToken);
      var result = await driver.PullAsync(Ctx, "app", "v1", null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = conn.GetRequests().Last(r => r.Method == "POST_STREAM");
      Assert.NotNull(request.Headers);
      Assert.True(request.Headers!.ContainsKey("X-Registry-Auth"));
    }

    [Fact]
    public async Task ServiceCreate_SendsRegistryAuthHeader()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/auth", 200, "{}");
      conn.SetupPost("/services/create", 200, @"{""ID"":""svc""}");
      var auth = new DockerApiAuthDriver(conn);
      var driver = new DockerApiServiceDriver(conn);
      driver.Initialize(Ctx);

      await auth.LoginAsync(Ctx, new RegistryLoginConfig
      {
        Server = "registry.example.com",
        Username = "me",
        Password = "secret"
      }, TestContext.Current.CancellationToken);
      var result = await driver.CreateAsync(Ctx,
          new ServiceCreateConfig { Name = "svc", Image = "registry.example.com/app:v1" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = conn.GetRequests().Last(r => r.Path.Contains("/services/create"));
      Assert.NotNull(request.Headers);
      Assert.True(request.Headers!.ContainsKey("X-Registry-Auth"));
    }

    [Fact]
    public async Task ServiceUpdate_SendsRegistryAuthHeader()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupPost("/auth", 200, "{}");
      conn.SetupGet("/services/svc", 200,
          @"{""ID"":""svc"",""Version"":{""Index"":1},""Spec"":{""Name"":""svc"",""TaskTemplate"":{""ContainerSpec"":{""Image"":""registry.example.com/app:v1""}}}}");
      conn.SetupPost("/services/svc/update", 200, "{}");
      var auth = new DockerApiAuthDriver(conn);
      var driver = new DockerApiServiceDriver(conn);
      driver.Initialize(Ctx);

      await auth.LoginAsync(Ctx, new RegistryLoginConfig
      {
        Server = "registry.example.com",
        Username = "me",
        Password = "secret"
      }, TestContext.Current.CancellationToken);
      var result = await driver.UpdateAsync(Ctx, "svc",
          new ServiceUpdateConfig { Replicas = 2 }, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = conn.GetRequests().Last(r => r.Path.Contains("/services/svc/update"));
      Assert.NotNull(request.Headers);
      Assert.True(request.Headers!.ContainsKey("X-Registry-Auth"));
    }

    [Fact]
    public async Task ExportAsync_MidStreamFailure_RemovesPartialFile()
    {
      Directory.CreateDirectory(".out");
      var outputPath = Path.Combine(".out", "ch6-export-partial.tar");
      File.Delete(outputPath);
      var conn = new MockDockerApiConnection();
      conn.SetupStreamReadThrows("/export", Encoding.UTF8.GetBytes("partial"), new IOException("boom"));
      var driver = new DockerApiContainerDriver(conn);
      driver.Initialize(Ctx);

      var result = await driver.ExportAsync(Ctx, "ctr", outputPath, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task BuildAsync_IncludesEmptyDirectoriesInContextTar()
    {
      var root = Path.Combine(".out", "ch6-build-context");
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
      Directory.CreateDirectory(Path.Combine(root, "empty"));
      await File.WriteAllTextAsync(Path.Combine(root, "Dockerfile"), "FROM scratch\n",
          TestContext.Current.CancellationToken);
      var conn = new MockDockerApiConnection();
      conn.SetupStream("/build", @"{""aux"":{""ID"":""sha256:abc""}}" + "\n");
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(Ctx);

      var result = await driver.BuildAsync(Ctx, new ImageBuildConfig { BuildContext = root },
          null!, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var body = conn.GetRequests().Last(r => r.Method == "POST_STREAM").BodyBytes;
      Assert.Contains("empty/", ReadTarEntryNames(body!));
    }

    [Fact]
    public async Task GetStreamAsync_HeaderRead_IsBoundedByConnectionTimeout()
    {
      var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      var accepted = AcceptAndWedgeAsync(listener);
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{port}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(100),
        RequestTimeout = TimeSpan.FromSeconds(30)
      });

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          conn.GetStreamAsync("/events", TestContext.Current.CancellationToken));

      listener.Stop();
      await accepted;
    }

    private static async Task AcceptAndWedgeAsync(TcpListener listener)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
      {
      }
    }

    private static IEnumerable<string> ReadTarEntryNames(byte[] tar)
    {
      for (var offset = 0; offset + 512 <= tar.Length;)
      {
        if (tar.Skip(offset).Take(512).All(b => b == 0))
          yield break;
        var name = Encoding.ASCII.GetString(tar, offset, 100).TrimEnd('\0');
        var sizeText = Encoding.ASCII.GetString(tar, offset + 124, 12).Trim('\0', ' ');
        var size = string.IsNullOrEmpty(sizeText) ? 0 : Convert.ToInt64(sizeText, 8);
        yield return name;
        offset += 512 + (int)(((size + 511) / 512) * 512);
      }
    }

    private sealed class ExposedDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection)
    {
      public Task<ApiResult> DeleteForTestAsync(string path, CancellationToken ct) =>
          DeleteAsync(path, ct);
    }

    private sealed class ThrowingConnection(Exception exception) : IDockerApiConnection
    {
      public string ApiVersion => "1.45";
      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) => throw exception;
      public Task<HttpResponseMessage> PostAsync(string path, HttpContent? content = null, CancellationToken ct = default) => throw exception;
      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) => throw exception;
      public Task<HttpResponseMessage> PutAsync(string path, HttpContent content, CancellationToken ct = default) => throw exception;
      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) => throw exception;
      public Task<Stream> GetStreamAsync(string path, CancellationToken ct = default) => throw exception;
      public Task<Stream> PostStreamAsync(string path, HttpContent? content = null, CancellationToken ct = default) => throw exception;
      public Task<Stream> PostStreamAsync(string path, HttpContent content, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) => throw exception;
      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
