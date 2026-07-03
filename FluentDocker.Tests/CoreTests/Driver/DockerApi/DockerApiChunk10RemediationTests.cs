using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiChunk10RemediationTests
  {
    private static DriverContext Ctx => new("docker-api-chunk10-test");

    [Fact]
    public async Task ExecAsync_MultibyteUtf8SplitAcrossFrames_DecodesOncePerStream()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-utf8""}");
      mock.SetupStreamBytes("/exec/exec-utf8/start", Combine(
          Frame(1, [0x68, 0xC3]),
          Frame(1, [0xA9, 0x6C, 0x6C, 0x6F])));
      mock.SetupGet("/exec/exec-utf8/json", 200, @"{""ExitCode"":0}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["printf"], Tty = false },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("héllo", result.Data.StdOut);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_MultibyteUtf8SplitAcrossFrames_DecodesOncePerStream()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Combine(
          Frame(1, [0x68, 0xC3]),
          Frame(1, [0xA9, 0x6C, 0x6C, 0x6F])));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      Assert.Single(entries);
      Assert.Equal("héllo", entries[0].Line);
      Assert.Equal(LogStreamSource.Stdout, entries[0].Source);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_InterleavedStdoutStderr_HasSeparateUtf8Decoders()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Combine(
          Frame(1, [0x6F, 0xC3]),
          Frame(2, [0x65, 0xC3]),
          Frame(1, [0xA9]),
          Frame(2, [0xA9])));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);

      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(
          Ctx, "ctr", new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      Assert.Equal(2, entries.Count);
      Assert.Equal("oé", entries[0].Line);
      Assert.Equal(LogStreamSource.Stdout, entries[0].Source);
      Assert.Equal("eé", entries[1].Line);
      Assert.Equal(LogStreamSource.Stderr, entries[1].Source);
    }

    [Fact]
    public async Task PullAsync_CancellationAfterProgress_ThrowsOperationCanceledException()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/images/create",
          @"{""status"":""one""}" + "\n" +
          @"{""status"":""two""}" + "\n");
      var driver = new DockerApiImageDriver(mock);
      driver.Initialize(Ctx);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var progress = new CancelProgress<ImagePullProgress>(cts);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(
          () => driver.PullAsync(Ctx, "alpine", "latest", progress, cts.Token));
    }

    [Fact]
    public async Task BuildAsync_CancellationAfterProgress_ThrowsOperationCanceledException()
    {
      var dir = CreateScratchDirectory("build-cancel");
      try
      {
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "Dockerfile"),
            "FROM scratch\n", TestContext.Current.CancellationToken);
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/build",
            @"{""stream"":""one""}" + "\n" +
            @"{""aux"":{""ID"":""sha256:deadbeef""}}" + "\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var progress = new CancelProgress<ImageBuildProgress>(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => driver.BuildAsync(Ctx,
                new ImageBuildConfig { BuildContext = dir.FullName }, progress, cts.Token));
      }
      finally
      {
        dir.Delete(recursive: true);
      }
    }

    [Fact]
    public async Task StreamEventsAsync_CancellationAfterFirstEvent_ThrowsOperationCanceledException()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupStream("/events",
          @"{""Type"":""container"",""Action"":""start"",""time"":1}" + "\n" +
          @"{""Type"":""container"",""Action"":""die"",""time"":2}" + "\n");
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await foreach (var _ in driver.StreamEventsAsync(Ctx, cancellationToken: cts.Token))
          cts.Cancel();
      });
    }

    [Fact]
    public async Task LoadAsync_CancellationAfterFirstLine_ThrowsOperationCanceledException()
    {
      var file = CreateScratchFile("load-cancel", "fake-tar");
      try
      {
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/images/load",
            @"{""stream"":""Loaded image: alpine:latest""}" + "\n" +
            @"{""stream"":""Loaded image: busybox:latest""}" + "\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
          var task = driver.LoadAsync(Ctx, file, cts.Token);
          cts.Cancel();
          await task;
        });
      }
      finally
      {
        var dir = Path.GetDirectoryName(file);
        File.Delete(file);
        if (!string.IsNullOrEmpty(dir))
          Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public async Task StreamLogEntriesAsync_CancellationAfterMuxFrame_ThrowsOperationCanceledException()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr/logs", Combine(Frame(1, "one"), Frame(1, "two")));
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(
            Ctx, "ctr", new StreamLogsConfig { Follow = false }, cts.Token))
          cts.Cancel();
      });
    }

    [Fact]
    public async Task StreamLogEntriesAsync_CancellationAfterRawLine_ThrowsOperationCanceledException()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr/json", 200, @"{""Config"":{""Tty"":true}}");
      mock.SetupStream("/containers/ctr/logs", "one\ntwo\n");
      var driver = new DockerApiStreamDriver(mock);
      driver.Initialize(Ctx);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await foreach (var _ in driver.StreamLogEntriesAsync(
            Ctx, "ctr", new StreamLogsConfig { Follow = false }, cts.Token))
          cts.Cancel();
      });
    }

    [Fact]
    public async Task NetworkListAsync_FilterNameWithQuoteAndBackslash_SendsValidJson()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/networks", 200, "[]");
      var driver = new DockerApiNetworkDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ListAsync(Ctx,
          new NetworkListFilter { Name = "a\"b\\c" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "GET");
      var decoded = Uri.UnescapeDataString(request.Path.Split("filters=")[1]);
      using var doc = JsonDocument.Parse(decoded);
      Assert.Equal("a\"b\\c", doc.RootElement.GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task VolumeListAsync_FilterNameWithQuoteAndBackslash_SendsValidJson()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/volumes", 200, @"{""Volumes"":[]}");
      var driver = new DockerApiVolumeDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ListAsync(Ctx,
          new VolumeListFilter { Name = "a\"b\\c" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var request = mock.GetRequests().Single(r => r.Method == "GET");
      var decoded = Uri.UnescapeDataString(request.Path.Split("filters=")[1]);
      using var doc = JsonDocument.Parse(decoded);
      Assert.Equal("a\"b\\c", doc.RootElement.GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task StopAsync_TimeoutLongerThanRequestTimeout_UsesLongRunningClient()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var server = ServeDelayedNoBodyResponseAsync(listener, TimeSpan.FromMilliseconds(150), cts.Token);

      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        RequestTimeout = TimeSpan.FromMilliseconds(50),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });
      var driver = new DockerApiContainerDriver(connection);
      driver.Initialize(Ctx);

      var result = await driver.StopAsync(Ctx, "ctr", timeout: 1, TestContext.Current.CancellationToken);
      await server;

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task BuildAsync_AfterLogin_SendsRegistryConfigHeader()
    {
      var dir = CreateScratchDirectory("build-auth");
      try
      {
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "Dockerfile"),
            "FROM registry.internal/base:latest\n", TestContext.Current.CancellationToken);
        var mock = new MockDockerApiConnection();
        mock.SetupPost("/auth", 200, "{}");
        mock.SetupStream("/build", @"{""aux"":{""ID"":""sha256:deadbeef""}}" + "\n");
        var auth = new DockerApiAuthDriver(mock);
        var image = new DockerApiImageDriver(mock);
        auth.Initialize(Ctx);
        image.Initialize(Ctx);

        var login = await auth.LoginAsync(Ctx, new RegistryLoginConfig
        {
          Server = "registry.internal",
          Username = "ci",
          Password = "secret"
        }, TestContext.Current.CancellationToken);
        var result = await image.BuildAsync(Ctx, new ImageBuildConfig
        {
          BuildContext = dir.FullName,
          Tags = { "registry.internal/app:latest" }
        }, null!, TestContext.Current.CancellationToken);

        Assert.True(login.Success, login.Error);
        Assert.True(result.Success, result.Error);
        var request = mock.GetRequests().Last(r => r.Method == "POST_STREAM");
        Assert.NotNull(request.Headers);
        var headerValue = request.Headers["X-Registry-Config"];
        var padded = headerValue.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        using var doc = JsonDocument.Parse(Convert.FromBase64String(padded));
        var registry = doc.RootElement.GetProperty("registry.internal");
        Assert.Equal("ci", registry.GetProperty("username").GetString());
      }
      finally
      {
        dir.Delete(recursive: true);
      }
    }

    [Fact]
    public async Task ExecAsync_InspectFailure_ReturnsFailedResponse()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/exec", 201, @"{""Id"":""exec-inspect""}");
      mock.SetupStreamBytes("/exec/exec-inspect/start", Frame(1, "ok"));
      mock.SetupGet("/exec/exec-inspect/json", 500, @"{""message"":""inspect failed""}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ExecAsync(Ctx, "ctr",
          new ExecConfig { Command = ["echo"], Tty = false },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.ExecFailed, result.ErrorCode);
      Assert.Contains("inspect failed", result.Error);
    }

    [Fact]
    public async Task CreateAsync_Ipv6HostBinding_StripsBracketsFromHostIp()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""ctr1""}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.CreateAsync(Ctx, new ContainerCreateConfig
      {
        Image = "busybox",
        PortBindings = { ["80/tcp"] = "[::1]:8080" }
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var body = mock.GetRequests().Single(r => r.Method == "POST").Body;
      Assert.Contains(@"""HostIp"":""::1""", body);
      Assert.DoesNotContain(@"""HostIp"":""[::1]""", body);
    }

    [Fact]
    public async Task BuildAsync_Pre1970Mtime_ClampsTarHeaderTimeToZero()
    {
      var dir = CreateScratchDirectory("build-mtime");
      try
      {
        var dockerfile = Path.Combine(dir.FullName, "Dockerfile");
        await File.WriteAllTextAsync(dockerfile, "FROM scratch\n", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(dockerfile, new DateTime(1960, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var mock = new MockDockerApiConnection();
        mock.SetupStream("/build", @"{""aux"":{""ID"":""sha256:deadbeef""}}" + "\n");
        var driver = new DockerApiImageDriver(mock);
        driver.Initialize(Ctx);

        var result = await driver.BuildAsync(Ctx,
            new ImageBuildConfig { BuildContext = dir.FullName }, null!,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
      }
      finally
      {
        dir.Delete(recursive: true);
      }
    }

    [Fact]
    public void DockerIgnoreFilter_BackslashEscapesWildcardOnLinux()
    {
      if (OperatingSystem.IsWindows())
        return;

      var filter = DockerIgnoreFilter.FromLines(["foo\\*.txt"]);

      Assert.True(filter.IsIgnored("foo*.txt"));
      Assert.False(filter.IsIgnored("foo123.txt"));
    }

    private static DirectoryInfo CreateScratchDirectory(string name)
    {
      var path = Path.Combine(".out", "chunk10", name + "-" + Guid.NewGuid().ToString("N"));
      return Directory.CreateDirectory(path);
    }

    private static string CreateScratchFile(string name, string content)
    {
      var dir = CreateScratchDirectory(name);
      var path = Path.Combine(dir.FullName, "input.tar");
      File.WriteAllText(path, content);
      return path;
    }

    private static async Task ServeDelayedNoBodyResponseAsync(
        TcpListener listener, TimeSpan delay, CancellationToken ct)
    {
      using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
      await using var stream = client.GetStream();
      using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
      while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct).ConfigureAwait(false)))
      {
      }

      await Task.Delay(delay, ct).ConfigureAwait(false);
      var header = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n");
      await stream.WriteAsync(header, ct).ConfigureAwait(false);
    }

    private sealed class CancelProgress<T>(CancellationTokenSource cts) : IProgress<T>
    {
      public void Report(T value) => cts.Cancel();
    }

    private static byte[] Frame(byte streamType, string payload) =>
        Frame(streamType, Encoding.UTF8.GetBytes(payload));

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
  }
}
