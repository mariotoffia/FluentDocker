using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>Tests for DockerApiConnection and DockerApiConnectionConfig.</summary>
  [Trait("Category", "Unit")]
  public partial class DockerApiConnectionTests
  {
    #region GetDefaultHost

    [Fact]
    public void GetDefaultHost_OnCurrentPlatform_ReturnsExpectedScheme()
    {
      // GetDefaultHost is internal static, invoke via reflection
      var method = typeof(DockerApiConnection).GetMethod(
          "GetDefaultHost",
          BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
      Assert.NotNull(method);

      var result = (string)method.Invoke(null, null)!;
      Assert.NotNull(result);

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        Assert.StartsWith("npipe:", result);
      else
        Assert.Equal("unix:///var/run/docker.sock", result);
    }

    #endregion

    #region Config Defaults

    [Fact]
    public void Config_DefaultValues_AreCorrect()
    {
      var config = new DockerApiConnectionConfig();

      Assert.Equal(TimeSpan.FromSeconds(30), config.ConnectionTimeout);
      Assert.Equal(TimeSpan.FromMinutes(5), config.RequestTimeout);
      Assert.True(config.VerifyTls);
      Assert.Null(config.ApiVersion);
      Assert.Null(config.Host);
      Assert.Null(config.CertificatePath);
    }

    [Fact]
    public void Config_CustomValues_AreRetained()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://remote:2375",
        CertificatePath = "/certs",
        VerifyTls = false,
        ConnectionTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromMinutes(2),
        ApiVersion = "1.43"
      };

      Assert.Equal("tcp://remote:2375", config.Host);
      Assert.Equal("/certs", config.CertificatePath);
      Assert.False(config.VerifyTls);
      Assert.Equal(TimeSpan.FromSeconds(10), config.ConnectionTimeout);
      Assert.Equal(TimeSpan.FromMinutes(2), config.RequestTimeout);
      Assert.Equal("1.43", config.ApiVersion);
    }

    #endregion

    #region Constructor - Transport Selection

    [Fact]
    public async Task Constructor_UnixScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "unix:///var/run/docker.sock",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public async Task Constructor_TcpScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public async Task Constructor_HttpsScheme_CreatesHandler()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "https://localhost:2376",
        VerifyTls = false,
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.NotNull(conn);
    }

    [Fact]
    public void Constructor_InvalidScheme_ThrowsArgumentException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "ftp://localhost",
        ApiVersion = "1.45"
      };

      Assert.Throws<ArgumentException>(() => new DockerApiConnection(config));
    }

    #endregion

    #region PingAsync

    [Fact]
    public async Task PingAsync_WhenNoDockerRunning_ReturnsFalse()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);
      var result = await conn.PingAsync(TestContext.Current.CancellationToken);

      Assert.False(result);
    }

    [Fact]
    public async Task PingAsync_WhenCallerTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
      // M14: a cancellation requested by the CALLER's token must propagate as an
      // OperationCanceledException, not be swallowed and reported as "unreachable" (false).
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      await using var conn = new DockerApiConnection(config);
      using var cts = new CancellationTokenSource();
      cts.Cancel(); // Caller cancels before pinging.

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PingAsync(cts.Token));
    }

    [Fact]
    public async Task PingAsync_WhenUnreachableWithLiveToken_ReturnsFalse()
    {
      // M14: a genuine connection failure (unreachable endpoint) must still be reported as
      // false even though the request timeout fires an internal cancellation — that internal
      // timeout must NOT be mistaken for caller cancellation.
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);
      using var cts = new CancellationTokenSource(); // Live, never cancelled.

      var result = await conn.PingAsync(cts.Token);

      Assert.False(result);
    }

    #endregion

    #region Unix Socket Connect Failure (M13)

    [Fact]
    public async Task UnixSocket_FailedConnect_ThrowsAndDoesNotLeaveLingeringState()
    {
      // M13: connecting a unix-domain socket to a nonexistent path must throw, and the
      // partially-created Socket must be disposed (not leaked). We cannot probe the private
      // Socket's disposed flag directly through the public surface, so we assert the next-best
      // deterministic behavior: the failed connect throws, and a SUBSEQUENT connect attempt on
      // the same connection still fails cleanly (no lingering/poisoned state) — every attempt
      // creates and disposes its own socket.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return; // Unix domain sockets are exercised on non-Windows platforms.

      var socketPath = Path.Combine(Path.GetTempPath(), $"fd-no-such-{Guid.NewGuid():N}.sock");
      var config = new DockerApiConnectionConfig
      {
        Host = $"unix://{socketPath}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);

      var first = await Record.ExceptionAsync(
          () => conn.GetAsync("/containers/json", TestContext.Current.CancellationToken));
      Assert.NotNull(first);

      // A second attempt must behave identically — the connection is not left in a broken
      // state by the disposed socket from the first failed connect.
      var second = await Record.ExceptionAsync(
          () => conn.GetAsync("/containers/json", TestContext.Current.CancellationToken));
      Assert.NotNull(second);
    }

    [Fact]
    public async Task UnixSocket_NonexistentPath_PingReturnsFalse()
    {
      // M13: the unix-socket connect-failure path (nonexistent path) must dispose the
      // partially-created Socket and surface as unreachable. Ping swallows the connect failure
      // and reports false (mirrors ModelApiConnection's UnixSocket_NonexistentPath test).
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return; // Unix domain sockets are exercised on non-Windows platforms.

      var socketPath = Path.Combine(Path.GetTempPath(), $"fd-no-such-{Guid.NewGuid():N}.sock");
      var config = new DockerApiConnectionConfig
      {
        Host = $"unix://{socketPath}",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);

      Assert.False(await conn.PingAsync(TestContext.Current.CancellationToken));
    }

    #endregion

    #region ApiVersion

    [Fact]
    public async Task ApiVersion_WhenSetInConfig_ReturnsConfigValue()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.43"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.Equal("1.43", conn.ApiVersion);
    }

    [Fact]
    public async Task ApiVersion_WhenNotSetInConfig_IsNull()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.Null(conn.ApiVersion);
    }

    #endregion

    #region Version Negotiation State

    [Fact]
    public async Task ApiVersion_WhenSetInConfig_SkipsNegotiation()
    {
      // When ApiVersion is pre-set, the negotiation state object should
      // reflect it immediately without needing a network call.
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375",
        ApiVersion = "1.45"
      };

      await using var conn = new DockerApiConnection(config);

      // The negotiation state should hold the configured version atomically.
      Assert.Equal("1.45", conn.ApiVersion);
      Assert.True(conn.IsVersionNegotiated);
    }

    [Fact]
    public async Task ApiVersion_WhenNotSetInConfig_NegotiationNotCompleted()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375"
      };

      await using var conn = new DockerApiConnection(config);

      // No version set and no negotiation yet
      Assert.Null(conn.ApiVersion);
      Assert.False(conn.IsVersionNegotiated);
    }

    #endregion



    #region M2 - Stream calls surface Docker error body

    [Fact]
    public async Task EnsureStreamSuccess_500WithDockerMessage_ThrowsWithReason()
    {
      // M2: a non-success stream response must surface Docker's {"message":...} body, not be
      // discarded by EnsureSuccessStatusCode(). Exercised via the private helper by reflection
      // (the public GetStreamAsync/PostStreamAsync paths require a live socket).
      var method = typeof(DockerApiConnection).GetMethod(
          "EnsureStreamSuccessAsync", BindingFlags.Static | BindingFlags.NonPublic);
      Assert.NotNull(method);

      using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
      {
        Content = new StringContent(
            @"{""message"":""boom""}", Encoding.UTF8, "application/json")
      };

      var task = (Task)method!.Invoke(
          null, new object[] { response, CancellationToken.None })!;
      var ex = await Assert.ThrowsAsync<HttpRequestException>(async () => await task);

      Assert.Contains("boom", ex.Message);
      Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task EnsureStreamSuccess_SuccessResponse_DoesNotThrow()
    {
      var method = typeof(DockerApiConnection).GetMethod(
          "EnsureStreamSuccessAsync", BindingFlags.Static | BindingFlags.NonPublic);
      Assert.NotNull(method);

      using var response = new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent("stream", Encoding.UTF8)
      };

      var task = (Task)method!.Invoke(
          null, new object[] { response, CancellationToken.None })!;
      await task; // must complete without throwing
    }

    #endregion

    #region M3 - Negotiation not cached on caller cancellation

    [Fact]
    public async Task NegotiateApiVersion_CallerCancelled_PropagatesAndDoesNotCache()
    {
      // M3: caller cancellation during version negotiation must propagate as OCE and must NOT
      // mark negotiation complete, so a later call retries instead of caching a degraded state.
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:2375"
      };

      await using var conn = new DockerApiConnection(config);
      Assert.False(conn.IsVersionNegotiated);

      var method = typeof(DockerApiConnection).GetMethod(
          "NegotiateApiVersionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(method);

      using var cts = new CancellationTokenSource();
      cts.Cancel();

      var task = (Task)method!.Invoke(conn, new object[] { cts.Token })!;
      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);

      Assert.False(conn.IsVersionNegotiated);
    }

    #endregion
  }
}
