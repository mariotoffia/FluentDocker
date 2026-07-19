using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Self-test for <see cref="MockModelApiConnection"/> (M2): replays a chat
  /// response, an SSE stream and a forced fault, and exercises mid-stream faults.
  /// </summary>
  [Trait("Category", "Unit")]
  public class MockModelApiConnectionTests
  {
    [Fact]
    public async Task ReplaysJsonResponse_AndCapturesRequestBody()
    {
      var conn = new MockModelApiConnection()
          .SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));

      using var body = new StringContent("{\"model\":\"ai/smollm2\"}", Encoding.UTF8, "application/json");
      using var resp = await conn.PostAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
      var content = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
      Assert.Contains("chat.completion", content);

      var request = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("ai/smollm2", request.Body);
      Assert.Contains("/chat/completions", request.Path);
    }

    [Fact]
    public async Task ReplaysSseStream()
    {
      var conn = new MockModelApiConnection()
          .SetupStream("/chat/completions", DmrFixtures.Load("chat.sse"));

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken);
      using var reader = new StreamReader(stream);
      var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

      Assert.Contains("data:", text);
      Assert.Contains("chat.completion.chunk", text);
      Assert.Contains("[DONE]", text);
    }

    [Fact]
    public async Task ForcedFault_ReturnsErrorStatus()
    {
      var conn = new MockModelApiConnection()
          .SetupPost("/embeddings", 400, "{\"error\":{\"message\":\"bad\"}}");

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      using var resp = await conn.PostAsync("/engines/v1/embeddings", body, TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task NoMatch_ReturnsNotFound()
    {
      var conn = new MockModelApiConnection();
      using var resp = await conn.GetAsync("/engines/v1/models", TestContext.Current.CancellationToken);
      Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task LaterSetupOverridesEarlier()
    {
      var conn = new MockModelApiConnection()
          .SetupGet("/models", 200, "{\"first\":true}")
          .SetupGet("/models", 200, "{\"second\":true}");

      using var resp = await conn.GetAsync("/engines/v1/models", TestContext.Current.CancellationToken);
      var content = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
      Assert.Contains("second", content);
    }

    [Fact]
    public async Task Ping_Configurable()
    {
      Assert.True(await new MockModelApiConnection().SetupPing(true).PingAsync(TestContext.Current.CancellationToken));
      Assert.False(await new MockModelApiConnection().SetupPing(false).PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StreamFault_ThrowsMidStream()
    {
      var conn = new MockModelApiConnection()
          .SetupStreamFault("/chat/completions", "data: {\"choices\":[]}\n\n", faultAfterBytes: 8);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken);

      await Assert.ThrowsAnyAsync<IOException>(async () =>
      {
        var buffer = new byte[64];
        // First read returns the prefix, subsequent read throws.
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
        await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
      });
    }

    [Fact]
    public void BaseAddress_DefaultsToHostTcp()
    {
      Assert.Equal(new Uri("http://localhost:12434"), new MockModelApiConnection().BaseAddress);
    }

    [Fact]
    public async Task GetAsync_CancelledToken_ThrowsAndRecordsNothing()
    {
      var conn = new MockModelApiConnection().SetupGet("/models", 200, "{}");
      var cancelled = new CancellationToken(canceled: true);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.GetAsync("/engines/v1/models", cancelled));
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task PostAsync_CancelledToken_ThrowsAndRecordsNothing()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, "{}");
      var cancelled = new CancellationToken(canceled: true);
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PostAsync("/engines/v1/chat/completions", body, cancelled));
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task DeleteAsync_CancelledToken_ThrowsAndRecordsNothing()
    {
      var conn = new MockModelApiConnection().SetupDelete("/models", 200, "{}");
      var cancelled = new CancellationToken(canceled: true);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.DeleteAsync("/models/ai/x", cancelled));
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task PostStreamAsync_CancelledToken_ThrowsAndRecordsNothing()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", "data: {}\n\n");
      var cancelled = new CancellationToken(canceled: true);
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PostStreamAsync("/engines/v1/chat/completions", body, cancelled));
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task PingAsync_CancelledToken_ThrowsAndRecordsNothing()
    {
      var conn = new MockModelApiConnection().SetupPing(true);
      var cancelled = new CancellationToken(canceled: true);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PingAsync(cancelled));
      Assert.Empty(conn.GetRequests());
    }

    [Fact]
    public async Task StallingStream_SyncRead_FailsFastAfterPrefix()
    {
      var conn = new MockModelApiConnection().SetupStreamStalling("/chat/completions", "data: ");
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken);

      var buffer = new byte[64];
      var prefixRead = stream.Read(buffer, 0, buffer.Length);
      Assert.Equal("data: ", Encoding.UTF8.GetString(buffer, 0, prefixRead));

      // The next sync read must throw fast (NotSupportedException), never block forever.
      Assert.Throws<NotSupportedException>(() => stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task StallingStream_AsyncRead_StallsUntilCancellation()
    {
      var conn = new MockModelApiConnection().SetupStreamStalling("/chat/completions", "data: ");
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken);

      var buffer = new byte[64];
      var prefixRead = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
      Assert.Equal("data: ", Encoding.UTF8.GetString(buffer, 0, prefixRead));

      using var cts = new CancellationTokenSource();
      cts.CancelAfter(TimeSpan.FromMilliseconds(50));
      // The stalling stream must throw on cancellation, never return — assert the count to
      // catch a (defective) silent return and to satisfy CA2022 (inexact-read).
      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        var read = await stream.ReadAsync(buffer, cts.Token);
        Assert.Fail($"ReadAsync returned {read} instead of throwing on cancellation");
      });
    }
  }
}
