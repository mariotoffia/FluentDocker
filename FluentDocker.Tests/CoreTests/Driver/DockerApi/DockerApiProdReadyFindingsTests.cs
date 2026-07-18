using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
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
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed partial class DockerApiProdReadyFindingsTests
  {
    private static DriverContext Ctx => new("docker-api-prod-ready-test");

    [Fact]
    public async Task GetLogsAsync_RawStreamContentType_DoesNotSniffAmbiguousPayload()
    {
      var raw = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 };
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      var server = ServeResponseAsync(listener, raw,
          ("Content-Type", "application/vnd.docker.raw-stream"));
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        RequestTimeout = TimeSpan.FromSeconds(2),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });
      var driver = new DockerApiContainerDriver(connection);
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "ctr",
          cancellationToken: TestContext.Current.CancellationToken);
      await server;

      Assert.True(result.Success, result.Error);
      Assert.Equal(Encoding.UTF8.GetString(raw), result.Data);
    }

    [Fact]
    public async Task NegotiationPing_UsesConnectionTimeout_NotRequestTimeout()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      var server = ServeDelayedPingThenOkAsync(listener, TimeSpan.FromMilliseconds(700),
          TestContext.Current.CancellationToken);
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ConnectionTimeout = TimeSpan.FromMilliseconds(50),
        RequestTimeout = TimeSpan.FromSeconds(5)
      });
      var sw = Stopwatch.StartNew();

      using var response = await connection.GetAsync(
          "/containers/json", TestContext.Current.CancellationToken);
      sw.Stop();
      await server;

      Assert.True(response.IsSuccessStatusCode);
      Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500),
          $"Negotiation took {sw.Elapsed}, expected the ping to be bounded by ConnectionTimeout.");
    }

    [Fact]
    public async Task GetStreamAsync_WithStreamIdleTimeout_ThrowsTimeoutExceptionWhenBodyStalls()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var server = ServeHeadersThenStallAsync(listener, cts.Token);
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        StreamIdleTimeout = TimeSpan.FromMilliseconds(50),
        RequestTimeout = TimeSpan.FromSeconds(5),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });

      await using var stream = await connection.GetStreamAsync(
          "/events", TestContext.Current.CancellationToken);
      var buffer = new byte[1];
      var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
          await stream.ReadAtLeastAsync(buffer, 1, throwOnEndOfStream: false,
              TestContext.Current.CancellationToken));
      cts.Cancel();
      await server;

      Assert.Contains("idle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_ShortRawPayloadWithFailedTtyDetect_YieldsRawLine()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/containers/short/logs", Encoding.UTF8.GetBytes("hi\n"));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      var entries = new List<LogEntry>();

      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "short", cancellationToken: TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      var only = Assert.Single(entries);
      Assert.Equal(LogStreamSource.Stdout, only.Source);
      Assert.Equal("hi", only.Line);
    }

    [Fact]
    public async Task CopyFromAsync_AttemptsToPreserveSafeSymlinkEntryAndLogsWarning()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-symlink", Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest") + Path.DirectorySeparatorChar;
      Directory.CreateDirectory(outputRoot);
      var tarBytes = await CreateSymlinkTarAsync("link.txt", "target.txt");
      var logs = new List<string>();
      var loggerFactory = new CollectingLoggerFactory(logs);
      var context = new DriverContext("docker-api-prod-ready-test") { LoggerFactory = loggerFactory };
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(context);

      var result = await driver.CopyFromAsync(context, "ctr", "/src", destination,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var link = new FileInfo(Path.Combine(destination, "link.txt"));
      if (link.LinkTarget == null)
      {
        Assert.Contains(logs, message => message.Contains(
            "Could not preserve Docker archive symlink", StringComparison.Ordinal));
      }
      else
      {
        Assert.Equal("target.txt", link.LinkTarget);
      }
      Assert.Contains(logs, message => message.Contains("link.txt", StringComparison.Ordinal) &&
          message.Contains("preserving symlink", StringComparison.Ordinal));
      Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public async Task CopyFromAsync_DirectorySymlink_MergesIntoExistingDestination()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyfrom-dir-symlink", Guid.NewGuid().ToString("N"));
      var destination = Path.Combine(outputRoot, "dest");
      Directory.CreateDirectory(destination);
      var tarBytes = await CreateDirectorySymlinkTarAsync();
      var logs = new List<string>();
      var loggerFactory = new CollectingLoggerFactory(logs);
      var context = new DriverContext("docker-api-prod-ready-test") { LoggerFactory = loggerFactory };
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/archive", tarBytes);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(context);

      var result = await driver.CopyFromAsync(context, "ctr", "/src", destination,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.True(Directory.Exists(Path.Combine(destination, "realdir")));
      var link = new DirectoryInfo(Path.Combine(destination, "linkdir"));
      Assert.Equal("realdir", link.LinkTarget);
      Assert.Contains(logs, message => message.Contains("linkdir", StringComparison.Ordinal) &&
          message.Contains("preserving symlink", StringComparison.Ordinal));
      Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public async Task CopyToAsync_DirectorySymlink_EmitsSymlinkTarEntry()
    {
      var outputRoot = Path.Combine(".out", "docker-api-copyto-symlink", Guid.NewGuid().ToString("N"));
      var source = Path.Combine(outputRoot, "src");
      Directory.CreateDirectory(source);
      await File.WriteAllTextAsync(Path.Combine(source, "target.txt"), "safe",
          TestContext.Current.CancellationToken);
      try
      {
        File.CreateSymbolicLink(Path.Combine(source, "link.txt"), "target.txt");
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
      {
        Directory.Delete(outputRoot, recursive: true);
        Assert.Skip("Symlink creation is not permitted on this platform.");
        return;
      }
      var mock = new MockDockerApiConnection();
      mock.SetupPut("/archive", 200, "{}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.CopyToAsync(Ctx, "ctr", source, "/dest",
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "PUT");
      Assert.True(TarContainsSymlink(request.BodyBytes!, "src/link.txt", "target.txt"));
      Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_MidStreamFailure_PreservesExistingOutput()
    {
      var outputRoot = Path.Combine(".out", "docker-api-save-atomic", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(outputRoot);
      var outputPath = Path.Combine(outputRoot, "image.tar");
      await File.WriteAllTextAsync(outputPath, "original",
          TestContext.Current.CancellationToken);
      var mock = new MockDockerApiConnection();
      mock.SetupStreamReadThrows("/images/get", Encoding.UTF8.GetBytes("partial"), new IOException("boom"));
      var driver = new DockerApiImageDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.SaveAsync(Ctx, ["alpine:latest"], outputPath,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal("original", await File.ReadAllTextAsync(
          outputPath, TestContext.Current.CancellationToken));
      Assert.Empty(Directory.EnumerateFiles(outputRoot, "*.tmp"));
      Directory.Delete(outputRoot, recursive: true);
    }

    [Fact]
    public async Task ExecAsync_EscapesDaemonSuppliedExecIdInStartAndInspectPaths()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec/with?chars""}");
      mock.SetupStreamBytes("/exec/", Array.Empty<byte>());
      mock.SetupGet("/json", 200, @"{""Running"":false,""ExitCode"":0}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["true"] }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains(mock.GetRequests(), r =>
          r.Method == "POST_STREAM" && r.Path.Contains("/exec/exec%2Fwith%3Fchars/start"));
      Assert.Contains(mock.GetRequests(), r =>
          r.Method == "GET" && r.Path.Contains("/exec/exec%2Fwith%3Fchars/json"));
    }

    [Fact]
    public async Task ExecAsync_StartFailure_UsesHttpRequestStatusCode()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec500""}");
      mock.SetupStreamThrows("/exec/exec500/start",
          new HttpRequestException("daemon rejected", null, HttpStatusCode.BadGateway));
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["true"] }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(502, result.ExitCode);
      Assert.Equal("502", result.ErrorContext!.Metadata["HttpStatusCode"]);
    }

    [Fact]
    public async Task AttachAsync_StreamOpenFailure_UsesHttpRequestStatusCode()
    {
      // DAPI-10: the attach failure path must surface the real HTTP status instead of 0.
      var mock = new MockDockerApiConnection();
      mock.SetupStreamThrows("/containers/ctr/attach",
          new HttpRequestException("no such container", null, HttpStatusCode.NotFound));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.AttachAsync(Ctx, "ctr",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.AttachFailed, result.ErrorCode);
      Assert.Equal(404, result.ExitCode);
      Assert.Equal("404", result.ErrorContext!.Metadata["HttpStatusCode"]);
    }

    [Fact]
    public async Task GetLogsAsync_TruncatedTailSplitsMultibyteChar_StripsLeadingReplacementChar()
    {
      // DAPI-14: the tail ring buffer evicts bytes, not characters, so a truncated tail can
      // start mid multi-byte sequence; the decoded tail must not begin with U+FFFD.
      var payload = new byte[CliOutputTruncation.DefaultTailChars + 2];
      payload[0] = (byte)'x';
      payload[1] = 0xC3; // 'e-acute' lead byte — evicted together with 'x', splitting the pair
      payload[2] = 0xA9; // orphaned continuation byte heading the retained window
      Array.Fill(payload, (byte)'a', 3, payload.Length - 3);
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":true}}");
      mock.SetupStreamBytes("/containers/ctr/logs", payload);
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.GetLogsAsync(Ctx, "ctr",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.StartsWith(CliOutputTruncation.Marker(CliOutputTruncation.DefaultTailChars),
          result.Data, StringComparison.Ordinal);
      var newlineEnd = result.Data!.IndexOf(Environment.NewLine, StringComparison.Ordinal)
          + Environment.NewLine.Length;
      var tail = result.Data[newlineEnd..];
      Assert.StartsWith("aaa", tail, StringComparison.Ordinal);
      Assert.DoesNotContain('\uFFFD', tail);
    }

    [Fact]
    public async Task GetStreamAsync_NonSuccessThenStalledBody_ThrowsWithinConnectionTimeout()
    {
      // DAPI-15: a daemon that sends a non-2xx status line and then stalls must not keep the
      // 32 KiB error-body read pending forever; it is bounded by ConnectionTimeout and the
      // HTTP status still surfaces.
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var server = ServeErrorHeadersThenStallAsync(listener, cts.Token);
      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(200),
        RequestTimeout = TimeSpan.FromSeconds(30)
      });
      var sw = Stopwatch.StartNew();

      var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
          connection.GetStreamAsync("/events", TestContext.Current.CancellationToken));
      sw.Stop();
      cts.Cancel();
      await server;

      Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
      Assert.Contains("Docker API 500", ex.Message, StringComparison.Ordinal);
      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
          $"error-body read was not bounded by ConnectionTimeout; elapsed {sw.Elapsed}");
    }

    [Fact]
    public async Task StreamEventsAsync_MissingTime_UsesReceiptTimeInsteadOfUnixEpoch()
    {
      var before = DateTime.UtcNow.AddSeconds(-1);
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/events",
          @"{""Type"":""container"",""Action"":""start"",""Actor"":{""ID"":""abc""}}" + "\n");
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var events = new List<ContainerEvent>();
      await foreach (var evt in driver.StreamEventsAsync(
          Ctx, new StreamEventsConfig { Until = "1" }, TestContext.Current.CancellationToken))
      {
        events.Add(evt);
      }

      var only = Assert.Single(events);
      Assert.True(only.Timestamp >= before, $"Expected receipt time, got {only.Timestamp:o}");
    }

    [Fact]
    public async Task DriverPack_SecondInitialize_ThrowsAndKeepsExistingConnection()
    {
      await using var pack = new DockerApiDriverPack();
      await pack.InitializeAsync(new DriverContext("docker-api", "tcp://localhost:2375"),
          TestContext.Current.CancellationToken);
      var firstConnection = pack.Connection;

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          pack.InitializeAsync(new DriverContext("docker-api-2", "tcp://localhost:2376"),
              TestContext.Current.CancellationToken));

      Assert.Same(firstConnection, pack.Connection);
    }

    private static async Task<byte[]> CreateSymlinkTarAsync(string name, string linkTarget)
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, name)
        {
          LinkName = linkTarget
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static async Task<byte[]> CreateDirectorySymlinkTarAsync()
    {
      await using var ms = new MemoryStream();
      await using (var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true))
      {
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, "realdir/file.txt")
        {
          DataStream = new MemoryStream(Encoding.UTF8.GetBytes("content"))
        }, TestContext.Current.CancellationToken);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.SymbolicLink, "linkdir")
        {
          LinkName = "realdir"
        }, TestContext.Current.CancellationToken);
      }
      return ms.ToArray();
    }

    private static bool TarContainsSymlink(byte[] tarBytes, string name, string linkTarget)
    {
      using var ms = new MemoryStream(tarBytes);
      using var reader = new TarReader(ms);
      TarEntry? entry;
      while ((entry = reader.GetNextEntry()) != null)
      {
        if (entry.EntryType == TarEntryType.SymbolicLink &&
            entry.Name == name &&
            entry.LinkName == linkTarget)
          return true;
      }
      return false;
    }

    private static async Task ServeResponseAsync(
        TcpListener listener, byte[] body, params (string Name, string Value)[] headers)
    {
      using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken)
          .ConfigureAwait(false);
      await using var stream = client.GetStream();
      await ReadHeadersAsync(stream, TestContext.Current.CancellationToken).ConfigureAwait(false);
      var headerText = new StringBuilder()
          .Append("HTTP/1.1 200 OK\r\n")
          .Append("API-Version: 1.45\r\n")
          .Append("Content-Length: ").Append(body.Length).Append("\r\n");
      foreach (var (name, value) in headers)
        headerText.Append(name).Append(": ").Append(value).Append("\r\n");
      headerText.Append("\r\n");
      await stream.WriteAsync(Encoding.ASCII.GetBytes(headerText.ToString()),
          TestContext.Current.CancellationToken).ConfigureAwait(false);
      await stream.WriteAsync(body, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private static async Task ServeDelayedPingThenOkAsync(
        TcpListener listener, TimeSpan pingDelay, CancellationToken ct)
    {
      var pingClient = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
      var pingTask = Task.Run(async () =>
      {
        using var client = pingClient;
        await using var stream = client.GetStream();
        var request = await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
        Assert.Contains("/_ping", request);
        await Task.Delay(pingDelay, ct).ConfigureAwait(false);
      }, ct);

      using (var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false))
      {
        await using var stream = client.GetStream();
        await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
        var body = Encoding.UTF8.GetBytes("[]");
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: application/json\r\n\r\n");
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
      }

      try
      {
        await pingTask.ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
    }

    private static async Task ServeErrorHeadersThenStallAsync(TcpListener listener, CancellationToken ct)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 500 Internal Server Error\r\nContent-Length: 4096\r\nContent-Type: application/json\r\n\r\n");
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
      catch (SocketException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }

    private static async Task ServeHeadersThenStallAsync(TcpListener listener, CancellationToken ct)
    {
      try
      {
        using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await ReadHeadersAsync(stream, ct).ConfigureAwait(false);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Type: application/json\r\n\r\n");
        await stream.WriteAsync(response, ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
      catch (SocketException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }

    private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken ct)
    {
      var buffer = new byte[1];
      var bytes = new List<byte>();
      while (true)
      {
        var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read == 0)
          break;
        bytes.Add(buffer[0]);
        if (bytes.Count >= 4 &&
            bytes[^4] == '\r' && bytes[^3] == '\n' &&
            bytes[^2] == '\r' && bytes[^1] == '\n')
          break;
      }
      return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private sealed class CollectingLoggerFactory(IList<string> messages) : ILoggerFactory
    {
      public void AddProvider(ILoggerProvider provider) { }
      public ILogger CreateLogger(string categoryName) => new CollectingLogger(messages);
      public void Dispose() { }
    }

    private sealed class CollectingLogger(IList<string> messages) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(
          LogLevel logLevel, EventId eventId, TState state, Exception? exception,
          Func<TState, Exception?, string> formatter)
      {
        if (logLevel >= LogLevel.Warning)
          messages.Add(formatter(state, exception));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }
  }
}
