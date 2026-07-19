using System.Text;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// A-M2: below the negotiated API 1.42 content-type gate, <c>GetLogsAsync</c> must resolve
  /// multiplexed-vs-raw via a container TTY inspect instead of byte-sniffing the first frame —
  /// a TTY container's binary output can coincidentally start with bytes that satisfy the
  /// stdcopy header shape (byte0 &lt;= 3, bytes1-3 zero), which the byte-sniff misparses as
  /// multiplexed and corrupts by stripping fake "headers" throughout the log.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class DockerApiLogsTtyInspectFallbackTests
  {
    private static DriverContext Ctx => new("docker-api-logs-tty-fallback-test");

    // Two 8-byte sequences that satisfy IsValidStdCopyHeader (byte0 <= 3, bytes1-3 == 0) purely
    // by coincidence, each followed by 3 payload-shaped bytes — stands in for raw TTY output that
    // happens to look like stdcopy frames.
    private static readonly byte[] TtyLookalikeBytes =
    [
      1, 0, 0, 0, 0, 0, 0, 3, (byte)'a', (byte)'b', (byte)'c',
      2, 0, 0, 0, 0, 0, 0, 3, (byte)'d', (byte)'e', (byte)'f'
    ];

    private static DockerApiContainerDriver CreateDriver(MockDockerApiConnection mock)
    {
      mock.ApiVersion = "1.41"; // pre-1.42: no authoritative stream Content-Type from the daemon.
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(Ctx);
      return driver;
    }

    [Fact]
    public async Task GetLogsAsync_Pre142TtyLookalikeHeader_InspectDetectsTty_ReturnsBytesUnstripped()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr1/json", 200, @"{""Config"":{""Tty"":true}}");
      mock.SetupStreamBytes("/containers/ctr1/logs", TtyLookalikeBytes);
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(
          Ctx, "ctr1", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      // Bytes must survive verbatim: no stdcopy-header stripping despite the coincidental match.
      Assert.Equal(Encoding.UTF8.GetString(TtyLookalikeBytes), result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_Pre142KnownNonTtyContainer_StillDemuxesNormally()
    {
      // Regression guard: a real multiplexed (non-TTY) stream must still be demuxed/stripped
      // correctly once inspect confirms Tty=false, not routed to the raw reader.
      byte[] frame1 = [1, 0, 0, 0, 0, 0, 0, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];
      byte[] frame2 = [2, 0, 0, 0, 0, 0, 0, 5, (byte)'w', (byte)'o', (byte)'r', (byte)'l', (byte)'d'];
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/containers/ctr2/json", 200, @"{""Config"":{""Tty"":false}}");
      mock.SetupStreamBytes("/containers/ctr2/logs", [.. frame1, .. frame2]);
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(
          Ctx, "ctr2", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("helloworld", result.Data);
    }

    [Fact]
    public async Task GetLogsAsync_Pre142InspectUnavailable_FallsBackToByteSniff()
    {
      // Documents the fix's boundary: when inspect itself cannot resolve TTY mode (here, no
      // mock is configured for the container so it 404s), behavior is unchanged from before
      // A-M2 — the byte-sniff heuristic still runs and still misparses this coincidental input.
      var mock = new MockDockerApiConnection();
      mock.SetupStreamBytes("/containers/ctr3/logs", TtyLookalikeBytes);
      var driver = CreateDriver(mock);

      var result = await driver.GetLogsAsync(
          Ctx, "ctr3", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("abcdef", result.Data);
    }
  }
}
