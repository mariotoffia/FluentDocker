using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
  public sealed class DockerApiChunk10ProdReadinessTests
  {
    private static DriverContext Ctx => new("docker-api-chunk10-prod-test");

    [Theory]
    [InlineData("http://engine.example:80", false, 80)]
    [InlineData("https://engine.example:443", true, 443)]
    [InlineData("tcp://engine.example", false, 2375)]
    [InlineData("tcp://engine.example:2376", true, 2376)]
    public void ResolveDockerPort_HonorsHttpPortsAndDefaultsOnlyTcpWithoutPort(
        string uri, bool useTls, int expected)
    {
      var method = typeof(DockerApiConnection).GetMethod(
          "ResolveDockerPort", BindingFlags.Static | BindingFlags.NonPublic);

      var actual = (int)method!.Invoke(null, [new Uri(uri), useTls])!;

      Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ExecAsync_TtyOutputBeyondCap_ReturnsTruncatedTailWithMarker()
    {
      var longOutput = new string('a', CliOutputTruncation.DefaultTailChars + 32) + "tail";
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-tty""}");
      mock.SetupStream("/exec/exec-tty/start", longOutput);
      mock.SetupGet("/exec/exec-tty/json", 200, @"{""ExitCode"":0}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["sh", "-c", "yes"], Tty = true },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.StartsWith(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars),
          result.Data.StdOut);
      Assert.EndsWith("tail", result.Data.StdOut);
      Assert.True(result.Data.StdOut.Length <= TruncatedOutputLimit());
    }

    [Fact]
    public async Task ExecAsync_MultiplexedOutputBeyondCap_ReturnsTruncatedTailWithMarker()
    {
      var longOutput = new string('b', CliOutputTruncation.DefaultTailChars + 32) + "tail";
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-mux""}");
      mock.SetupStreamBytes("/exec/exec-mux/start", Frame(1, longOutput));
      mock.SetupGet("/exec/exec-mux/json", 200, @"{""ExitCode"":0}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["sh", "-c", "yes"], Tty = false },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.StartsWith(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars),
          result.Data.StdOut);
      Assert.EndsWith("tail", result.Data.StdOut);
      Assert.True(result.Data.StdOut.Length <= TruncatedOutputLimit());
    }

    [Fact]
    public async Task BuildAsync_OutputBeyondCap_ReturnsTruncatedTailWithMarker()
    {
      var dir = CreateScratchDirectory("build-output-tail");
      try
      {
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "Dockerfile"),
            "FROM scratch\n", TestContext.Current.CancellationToken);
        var longOutput = new string('c', CliOutputTruncation.DefaultTailChars + 32) + "tail";
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/build",
            "{\"stream\":\"" + longOutput + "\\n\"}\n" +
            "{\"aux\":{\"ID\":\"sha256:deadbeef\"}}\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);

        var result = await driver.BuildAsync(Ctx,
            new ImageBuildConfig { BuildContext = dir.FullName }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var output = string.Join('\n', result.Data.Output);
        Assert.StartsWith(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars), output);
        Assert.EndsWith("tail", output);
        Assert.True(output.Length <= TruncatedOutputLimit());
      }
      finally
      {
        dir.Delete(recursive: true);
      }
    }

    [Fact]
    public async Task StreamLogEntriesAsync_PayloadReadIOException_IsDriverException()
    {
      var prefix = FrameHeader(1, 16);
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamReadThrows("/containers/ctr/logs", prefix, new IOException("daemon restarted"));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var ex = await Assert.ThrowsAsync<DriverException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(
            Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Contains("daemon restarted", ex.Message);
      Assert.Equal(ErrorCodes.Api.ServerError, ex.ErrorCode);
    }

    [Fact]
    public async Task ExportAsync_StreamFailure_PreservesPreexistingOutputFile()
    {
      var dir = CreateScratchDirectory("export-preserve");
      var output = Path.Combine(dir.FullName, "container.tar");
      await File.WriteAllTextAsync(output, "keep me", TestContext.Current.CancellationToken);
      var mock = new MockDockerApiConnection();
      mock.SetupStreamReadThrows("/export", Encoding.UTF8.GetBytes("partial"),
          new IOException("daemon restarted"));
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExportAsync(Ctx, "ctr", output,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal("keep me", await File.ReadAllTextAsync(output,
          TestContext.Current.CancellationToken));
      Assert.Empty(Directory.EnumerateFiles(dir.FullName, "*.tmp"));
      dir.Delete(recursive: true);
    }

    [Theory]
    [InlineData("logs")]
    [InlineData("pull")]
    [InlineData("push")]
    public async Task StreamingOpenHttpRequestException_PreservesHttpStatus(string operation)
    {
      var ex = new HttpRequestException("missing", null, HttpStatusCode.NotFound);
      var mock = new MockDockerApiConnection();
      if (operation == "logs")
      {
        mock.SetupStreamThrows("/logs", ex);
        var driver = new DockerApiContainerDriver(mock);
        driver.Initialize(Ctx);
        var result = await driver.GetLogsAsync(Ctx, "ctr",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(404, result.ExitCode);
        Assert.Equal("404", result.ErrorContext!.Metadata["HttpStatusCode"]);
      }
      else if (operation == "pull")
      {
        mock.SetupStreamThrows("/images/create", ex);
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);
        var result = await driver.PullAsync(Ctx, "missing", "latest", null!,
            TestContext.Current.CancellationToken);
        Assert.Equal(404, result.ExitCode);
        Assert.Equal("404", result.ErrorContext!.Metadata["HttpStatusCode"]);
      }
      else
      {
        mock.SetupStreamThrows("/push", ex);
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);
        var result = await driver.PushAsync(Ctx, "missing", null!,
            TestContext.Current.CancellationToken);
        Assert.Equal(404, result.ExitCode);
        Assert.Equal("404", result.ErrorContext!.Metadata["HttpStatusCode"]);
      }
    }

    [Fact]
    public async Task GetLogsAsync_InvalidSecondStdcopyHeader_FailsInsteadOfReinterpreting()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/logs", Combine(Frame(1, "ok"), InvalidHeaderFrame("bad")));
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "ctr",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DockerIgnoreFilter_UsesNonBacktrackingRegexForPathologicalStars()
    {
      var filter = DockerIgnoreFilter.FromLines(["**/**/**/**/**/needle"]);
      var rules = typeof(DockerIgnoreFilter).GetField("_rules",
          BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(filter) as System.Collections.IEnumerable;
      var rule = rules!.Cast<object>().Single();
      var regex = (Regex)rule.GetType().GetProperty("Pattern")!.GetValue(rule)!;

      Assert.True((regex.Options & RegexOptions.NonBacktracking) != 0);
      Assert.False(filter.IsIgnored(new string('a', 4096)));
    }

    [Fact]
    public async Task GetLogsAsync_TtfbTimeout_ReportsConnectionTimeout()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var server = ServeDelayedHeadersAsync(listener, TimeSpan.FromMilliseconds(250), cts.Token);
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(50),
        RequestTimeout = TimeSpan.FromMinutes(5)
      });
      var driver = new DockerApiContainerDriver(connection);
      driver.Initialize(new DriverContext("docker-api-timeout-test"));

      var result = await driver.GetLogsAsync(Ctx, "ctr",
          cancellationToken: TestContext.Current.CancellationToken);
      cts.Cancel();
      await server;

      Assert.False(result.Success);
      Assert.Equal(408, result.ExitCode);
      Assert.Contains("connection/TTFB timed out after 00:00:00.050", result.Error);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("post")]
    public async Task StreamOpen_ReadAsStreamFailure_DisposesResponse(string method)
    {
      var content = new ThrowingStreamContent();
      var client = new HttpClient(new SingleResponseHandler(
          new HttpResponseMessage(HttpStatusCode.OK) { Content = content }))
      {
        BaseAddress = new Uri("http://docker.test")
      };
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = "tcp://127.0.0.1:1",
        ApiVersion = "1.45"
      });
      var original = ReplaceLongRunningClient(connection, client);

      await Assert.ThrowsAsync<IOException>(() => method == "get"
          ? connection.GetStreamAsync("/containers/ctr/logs", TestContext.Current.CancellationToken)
          : connection.PostStreamAsync("/build", null!, TestContext.Current.CancellationToken));

      original.Dispose();
      Assert.True(content.IsDisposed);
    }

    [Fact]
    public void MapHttpErrorCode_403_MapsToForbidden()
    {
      Assert.Equal(ErrorCodes.Api.Forbidden, ExposedDriver.Map(403));
    }

    [Fact]
    public async Task ServiceUpdateAsync_VersionConflict_ReinspectsAndRetriesOnce()
    {
      var conn = new ServiceConflictConnection();
      var driver = new DockerApiServiceDriver(conn);
      driver.Initialize(Ctx);

      var result = await driver.UpdateAsync(Ctx, "svc-abc",
          new ServiceUpdateConfig { Replicas = 5 }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(2, conn.GetCount);
      Assert.Equal(2, conn.PostCount);
      Assert.Contains(conn.PostPaths, p => p.Contains("version=42"));
      Assert.Contains(conn.PostPaths, p => p.Contains("version=43"));
    }

    private static DirectoryInfo CreateScratchDirectory(string name)
    {
      var path = Path.Combine(".out", "chunk10-prod", name + "-" + Guid.NewGuid().ToString("N"));
      return Directory.CreateDirectory(path);
    }

    private static async Task ServeDelayedHeadersAsync(
        TcpListener listener, TimeSpan delay, CancellationToken ct)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct).ConfigureAwait(false)))
        {
        }
        await Task.Delay(delay, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
      }
      catch (IOException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }

    private static HttpClient ReplaceLongRunningClient(
        DockerApiConnection connection, HttpClient client)
    {
      var field = typeof(DockerApiConnection).GetField(
          "_longRunningHttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
      var original = (HttpClient)field.GetValue(connection)!;
      field.SetValue(connection, client);
      return original;
    }

    private static int TruncatedOutputLimit() =>
        CliOutputTruncation.DefaultTailChars +
        CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars).Length +
        Environment.NewLine.Length;

    private static byte[] Frame(byte streamType, string payload) =>
        Frame(streamType, Encoding.UTF8.GetBytes(payload));

    private static byte[] Frame(byte streamType, byte[] payload)
    {
      var frame = FrameHeader(streamType, payload.Length).Concat(payload).ToArray();
      return frame;
    }

    private static byte[] FrameHeader(byte streamType, int payloadLength)
    {
      var frame = new byte[8];
      frame[0] = streamType;
      frame[4] = (byte)((payloadLength >> 24) & 0xFF);
      frame[5] = (byte)((payloadLength >> 16) & 0xFF);
      frame[6] = (byte)((payloadLength >> 8) & 0xFF);
      frame[7] = (byte)(payloadLength & 0xFF);
      return frame;
    }

    private static byte[] InvalidHeaderFrame(string payload)
    {
      var bytes = Encoding.UTF8.GetBytes(payload);
      var frame = FrameHeader(9, bytes.Length).Concat(bytes).ToArray();
      return frame;
    }

    private static byte[] Combine(params byte[][] frames)
    {
      var combined = new byte[frames.Sum(static f => f.Length)];
      var offset = 0;
      foreach (var frame in frames)
      {
        Array.Copy(frame, 0, combined, offset, frame.Length);
        offset += frame.Length;
      }
      return combined;
    }

    private sealed class ExposedDriver(IDockerApiConnection conn) : DockerApiDriverBase(conn)
    {
      public static string Map(int statusCode) => MapHttpErrorCode(statusCode);
    }

    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
      protected override Task<HttpResponseMessage> SendAsync(
          HttpRequestMessage request, CancellationToken cancellationToken) =>
          Task.FromResult(response);
    }

    private sealed class ThrowingStreamContent : HttpContent
    {
      public bool IsDisposed { get; private set; }

      protected override Task SerializeToStreamAsync(
          Stream stream, TransportContext context) =>
          Task.CompletedTask;

      protected override bool TryComputeLength(out long length)
      {
        length = 0;
        return false;
      }

      protected override Task<Stream> CreateContentReadStreamAsync() =>
          throw new IOException("content stream failed");

      protected override Task<Stream> CreateContentReadStreamAsync(
          CancellationToken cancellationToken) =>
          throw new IOException("content stream failed");

      protected override void Dispose(bool disposing)
      {
        IsDisposed = true;
        base.Dispose(disposing);
      }
    }

    private sealed class ServiceConflictConnection : IDockerApiConnection
    {
      public int GetCount { get; private set; }
      public int PostCount { get; private set; }
      public List<string> PostPaths { get; } = [];
      public string ApiVersion => "1.45";

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
      {
        GetCount++;
        var version = GetCount == 1 ? 42 : 43;
        var json = @"{""ID"":""svc-abc"",""Version"":{""Index"":" + version +
            @"},""Spec"":{""Name"":""web"",""TaskTemplate"":{""ContainerSpec"":{""Image"":""nginx:latest""}},""Mode"":{""Replicated"":{""Replicas"":3}}}}";
        return Task.FromResult(Response(HttpStatusCode.OK, json));
      }

      public Task<HttpResponseMessage> PostAsync(
          string path, HttpContent content = null!, CancellationToken ct = default) =>
          PostAsync(path, content, null!, ct);

      public Task<HttpResponseMessage> PostAsync(
          string path, HttpContent content,
          IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
      {
        PostCount++;
        PostPaths.Add(path);
        var status = PostCount == 1 ? HttpStatusCode.Conflict : HttpStatusCode.OK;
        var body = PostCount == 1
            ? @"{""message"":""update out of sequence""}"
            : "{}";
        return Task.FromResult(Response(status, body));
      }

      public Task<HttpResponseMessage> HeadAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));

      public Task<HttpResponseMessage> PutAsync(
          string path, HttpContent content, CancellationToken ct = default) =>
          Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));

      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));

      public Task<Stream> GetStreamAsync(string path, CancellationToken ct = default) =>
          Task.FromResult<Stream>(new MemoryStream());

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content = null!, CancellationToken ct = default) =>
          Task.FromResult<Stream>(new MemoryStream());

      public Task<Stream> PostStreamAsync(
          string path, HttpContent content,
          IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
          Task.FromResult<Stream>(new MemoryStream());

      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);

      public ValueTask DisposeAsync() => ValueTask.CompletedTask;

      private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
          new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
  }
}
