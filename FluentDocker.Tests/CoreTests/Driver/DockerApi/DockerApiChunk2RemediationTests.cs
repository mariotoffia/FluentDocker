using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiChunk2RemediationTests
  {
    private static DriverContext Ctx => new("docker-api-chunk2-test");

    [Fact]
    public async Task PostStreamAsync_WithRequestContent_DoesNotApplyHeaderTimeout()
    {
      await using var server = await LoopbackHttpServer.StartAsync(async req =>
      {
        if (req.Path == "/v1.45/upload")
          await Task.Delay(250, TestContext.Current.CancellationToken);
        return LoopbackHttpServer.Ok("ok");
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(100),
        RequestTimeout = TimeSpan.FromSeconds(5)
      });

      await using var stream = await conn.PostStreamAsync(
          "/upload", new StringContent("payload"), TestContext.Current.CancellationToken)
          ;

      using var reader = new StreamReader(stream);
      Assert.Equal("ok", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NegotiateApiVersion_ClampsDaemonMaxToClientMax()
    {
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
          return Task.FromResult(LoopbackHttpServer.Ok("OK", ("API-Version", "9.99")));
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      using var response = await conn.GetAsync("/version", TestContext.Current.CancellationToken)
          ;

      Assert.Equal("1.45", conn.ApiVersion);
      Assert.Contains("/v1.45/version", server.Paths);
    }

    [Fact]
    public async Task NegotiateApiVersion_DaemonBelowFloor_FailsClearly()
    {
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
          return Task.FromResult(LoopbackHttpServer.Ok("OK"));
        if (req.Path == "/version")
          return Task.FromResult(LoopbackHttpServer.Ok(
              @"{""ApiVersion"":""1.20"",""MinAPIVersion"":""1.12""}"));
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          conn.GetAsync("/info", TestContext.Current.CancellationToken));

      Assert.Contains("daemon API version 1.20 is too old", ex.Message);
      Assert.Contains("need >= 1.24", ex.Message);
    }

    [Fact]
    public async Task NegotiateApiVersion_PingFailure_PinsClientDefaultVersion()
    {
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
          return Task.FromResult(LoopbackHttpServer.Response(
              HttpStatusCode.InternalServerError, "nope"));
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      using var response = await conn.GetAsync("/info", TestContext.Current.CancellationToken)
          ;

      Assert.Equal("1.45", conn.ApiVersion);
      Assert.Contains("/v1.45/info", server.Paths);
    }

    [Fact]
    public async Task NegotiateApiVersion_RecoversAfterTransientPingFailure()
    {
      var pings = 0;
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
        {
          var n = Interlocked.Increment(ref pings);
          return Task.FromResult(n == 1
              ? LoopbackHttpServer.Response(HttpStatusCode.InternalServerError, "starting")
              : LoopbackHttpServer.Ok("OK", ("API-Version", "1.41")));
        }
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      (await conn.GetAsync("/info", TestContext.Current.CancellationToken)).Dispose();
      Assert.Equal("1.45", conn.ApiVersion);

      (await conn.GetAsync("/info", TestContext.Current.CancellationToken)).Dispose();

      Assert.Equal("1.41", conn.ApiVersion);
      Assert.Contains("/v1.41/info", server.Paths);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_MultiplexedContentType_SkipsInspect()
    {
      var frame = Frame(1, Encoding.UTF8.GetBytes("hello\n"));
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path.Contains("/json", StringComparison.Ordinal))
          return Task.FromResult(LoopbackHttpServer.Response(HttpStatusCode.InternalServerError, "inspect forbidden"));
        return Task.FromResult(LoopbackHttpServer.Ok(
            frame, ("Content-Type", "application/vnd.docker.multiplexed-stream")));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });
      var driver = new DockerApiStreamDriver(conn);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        entries.Add(entry);

      Assert.Single(entries);
      Assert.Equal("hello", entries[0].Line);
      Assert.DoesNotContain(server.Paths, p => p.Contains("/json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamLogEntriesAsync_UnknownContentType_FallsBackToInspect()
    {
      var frame = Frame(1, Encoding.UTF8.GetBytes("hi\n"));
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path.Contains("/json", StringComparison.Ordinal))
          return Task.FromResult(LoopbackHttpServer.Ok(@"{""Config"":{""Tty"":false}}"));
        return Task.FromResult(LoopbackHttpServer.Ok(frame, ("Content-Type", "text/plain")));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });
      var driver = new DockerApiStreamDriver(conn);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        entries.Add(entry);

      Assert.Single(entries);
      Assert.Equal("hi", entries[0].Line);
      Assert.Contains(server.Paths, p => p.Contains("/json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamLogEntriesAsync_OlderApiVersion_FallsBackToInspect()
    {
      var mock = new MockDockerApiConnection { ApiVersion = "1.41" };
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Frame(1, Encoding.UTF8.GetBytes("old\n")));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      await foreach (var _ in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
      {
      }

      Assert.Contains(mock.GetRequests(), r => r.Method == "GET" && r.Path.Contains("/json"));
    }

    [Fact]
    public async Task StreamLogEntriesAsync_LineSplitAcrossFrames_EmitsOneEntry()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Combine(
          Frame(1, Encoding.UTF8.GetBytes("hel")),
          Frame(1, Encoding.UTF8.GetBytes("lo\n"))));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        entries.Add(entry);

      var only = Assert.Single(entries);
      Assert.Equal("hello", only.Line);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_LongLineSplitAcrossFrames_IsNotFragmented()
    {
      var line = new string('x', 17 * 1024);
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Combine(
          Frame(1, Encoding.UTF8.GetBytes(line[..9000])),
          Frame(1, Encoding.UTF8.GetBytes(line[9000..] + "\n"))));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        entries.Add(entry);

      var only = Assert.Single(entries);
      Assert.Equal(line, only.Line);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_ManyLinesInSingleFrame_EmitsEachInOrder()
    {
      var payload = new StringBuilder();
      for (var i = 0; i < 20000; i++)
        payload.Append("line-").Append(i).Append('\n');
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs",
          Frame(1, Encoding.UTF8.GetBytes(payload.ToString())));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", cancellationToken: TestContext.Current.CancellationToken))
        entries.Add(entry);

      Assert.Equal(20000, entries.Count);
      Assert.Equal("line-0", entries[0].Line);
      Assert.Equal("line-19999", entries[^1].Line);
    }

    [Fact]
    public async Task CopyToAsync_DirectoryPrefixesEntriesWithSourceBaseName()
    {
      var root = Path.Combine(".out", "docker-api-copyto-dir-prefix");
      var source = Path.Combine(root, "hostdir");
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
      Directory.CreateDirectory(source);
      await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "payload",
          TestContext.Current.CancellationToken);
      var mock = new MockDockerApiConnection();
      mock.SetupPut("/containers/ctr/archive", 200, "{}");
      var driver = CreateContainerDriver(mock);

      var result = await driver.CopyToAsync(
          Ctx, "ctr", source, "/dest", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Last(r => r.Method == "PUT").BodyBytes!;
      Assert.Contains("hostdir/file.txt", ReadTarEntryNames(body));
    }

    [Fact]
    public async Task CopyFromAsync_DirectoryFailureLeavesDestinationUnchanged()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-atomic");
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      if (Directory.Exists(outputRoot))
        Directory.Delete(outputRoot, recursive: true);
      Directory.CreateDirectory(destination);
      var tarBytes = await CreateTarBytesAsync(
          ("first.txt", "partial"), ("../escape.txt", "escape"));
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = CreateContainerDriver(mock);

      var result = await driver.CopyFromAsync(
          Ctx, "ctr", "/tmp/files", destination, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.False(File.Exists(Path.Combine(destination, "first.txt")));
      Assert.False(File.Exists(Path.Combine(outputRoot, "escape.txt")));
    }

    [Fact]
    public async Task DockerApiDriverPack_DisposeClearsRegistryCredentials()
    {
      var connection = new MockDockerApiConnection();
      var registryAuth = typeof(DockerApiDriverBase).Assembly.GetType(
          "FluentDocker.Drivers.Docker.Api.Components.DockerApiRegistryAuth")!;
      var config = new RegistryLoginConfig
      {
        Username = "u",
        Password = "p",
        Server = "registry.example"
      };
      registryAuth.GetMethod("Store")!.Invoke(null, [connection, config]);
      Assert.NotNull(registryAuth.GetMethod("HeaderFor")!.Invoke(
          null, [connection, "registry.example/app:latest"]));
      var pack = new DockerApiDriverPack();
      typeof(DockerApiDriverPack).GetField(
          "_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
          .SetValue(pack, connection);

      await pack.DisposeAsync();

      Assert.Null(registryAuth.GetMethod("HeaderFor")!.Invoke(
          null, [connection, "registry.example/app:latest"]));
    }

    private static DockerApiContainerDriver CreateContainerDriver(MockDockerApiConnection mock)
    {
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      return driver;
    }

    private static byte[] Frame(byte streamType, byte[] payload)
    {
      var frame = new byte[8 + payload.Length];
      frame[0] = streamType;
      frame[4] = (byte)((payload.Length >> 24) & 0xFF);
      frame[5] = (byte)((payload.Length >> 16) & 0xFF);
      frame[6] = (byte)((payload.Length >> 8) & 0xFF);
      frame[7] = (byte)(payload.Length & 0xFF);
      Array.Copy(payload, 0, frame, 8, payload.Length);
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

    private static IEnumerable<string> ReadTarEntryNames(byte[] tar)
    {
      using var ms = new MemoryStream(tar);
      using var reader = new TarReader(ms, leaveOpen: false);
      TarEntry? tarEntry;
      while ((tarEntry = reader.GetNextEntry()) != null)
        yield return tarEntry.Name;
    }

    private static async Task<byte[]> CreateTarBytesAsync(params (string Name, string Content)[] entries)
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        foreach (var (name, content) in entries)
        {
          await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, name)
          {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
          }, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
      }
      return ms.ToArray();
    }

    private sealed record Request(string Path, byte[] Body);

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
      private readonly TcpListener _listener;
      private readonly Func<Request, Task<byte[]>> _handler;
      private readonly Task _acceptLoop;
      private readonly List<string> _paths = [];

      private LoopbackHttpServer(TcpListener listener, Func<Request, Task<byte[]>> handler)
      {
        _listener = listener;
        _handler = handler;
        _acceptLoop = Task.Run(AcceptLoopAsync);
      }

      public string BaseAddress => $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
      public IReadOnlyList<string> Paths => _paths;

      public static Task<LoopbackHttpServer> StartAsync(Func<Request, Task<byte[]>> handler)
      {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new LoopbackHttpServer(listener, handler));
      }

      public static byte[] Ok(string body, params (string Name, string Value)[] headers) =>
          Response(HttpStatusCode.OK, Encoding.UTF8.GetBytes(body), headers);

      public static byte[] Ok(byte[] body, params (string Name, string Value)[] headers) =>
          Response(HttpStatusCode.OK, body, headers);

      public static byte[] Response(HttpStatusCode status, string body) =>
          Response(status, Encoding.UTF8.GetBytes(body));

      public static byte[] Response(
          HttpStatusCode status, byte[] body, params (string Name, string Value)[] headers)
      {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append((int)status).Append(' ').Append(status).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Connection: close\r\n");
        foreach (var (name, value) in headers)
          sb.Append(name).Append(": ").Append(value).Append("\r\n");
        sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString()).Concat(body).ToArray();
      }

      public async ValueTask DisposeAsync()
      {
        _listener.Stop();
        try
        {
          await _acceptLoop.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
      }

      private async Task AcceptLoopAsync()
      {
        while (true)
        {
          using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
          await using var stream = client.GetStream();
          var request = await ReadRequestAsync(stream).ConfigureAwait(false);
          _paths.Add(request.Path);
          var response = await _handler(request).ConfigureAwait(false);
          await stream.WriteAsync(response, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
      }

      private static async Task<Request> ReadRequestAsync(NetworkStream stream)
      {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < 65536)
        {
          var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken)
              .ConfigureAwait(false);
          if (read == 0)
            break;
          bytes.Add(buffer[0]);
          if (bytes.Count >= 4 &&
              bytes[^4] == '\r' && bytes[^3] == '\n' &&
              bytes[^2] == '\r' && bytes[^1] == '\n')
            break;
        }
        var headerText = Encoding.ASCII.GetString(bytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var path = lines[0].Split(' ')[1];
        var length = lines
            .Where(static l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(static l => int.Parse(
                l["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture))
            .FirstOrDefault();
        var body = new byte[length];
        var total = 0;
        while (total < length)
        {
          var read = await stream.ReadAsync(
              body.AsMemory(total, length - total), TestContext.Current.CancellationToken)
              .ConfigureAwait(false);
          if (read == 0)
            break;
          total += read;
        }
        return new Request(path, body);
      }
    }
  }
}
