using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  public partial class ModelApiConnectionTests
  {
    [Fact]
    public async Task PostStreamAsync_HeaderWait_UsesStreamFirstByteTimeout_AsTypedTimeout()
    {
      using var handler = new NeverHeadersHandler();
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig
          {
            StreamFirstByteTimeout = TimeSpan.FromMilliseconds(50),
            StreamReadIdleTimeout = TimeSpan.FromSeconds(30)
          });

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(
          () => conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task PostStreamAsync_HeaderWait_CallerCancellationStaysOperationCanceled()
    {
      using var handler = new NeverHeadersHandler();
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig { StreamFirstByteTimeout = TimeSpan.FromSeconds(30) });
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PostStreamAsync("/x", body, cts.Token));
    }

    [Fact]
    public async Task HandlerConfigOverload_AppliesRequestTimeout()
    {
      using var handler = new DelayHandler(TimeSpan.FromSeconds(5), () => Json(HttpStatusCode.OK, "{}"));
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig { RequestTimeout = TimeSpan.FromMilliseconds(50) });

      await Assert.ThrowsAsync<TimeoutException>(() => conn.GetAsync("/x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LegacyHandlerConstructor_AcceptsNullLoggerFactory()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      await using var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);

      Assert.Equal(new Uri("http://localhost:12434"), conn.BaseAddress);
    }

    [Fact]
    public async Task PostStreamAsync_NonSuccessErrorBody_UsesStreamIdleTimeout_AsTypedTimeout()
    {
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
      {
        Content = new StreamContent(new NeverReadStream())
      });
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig { StreamReadIdleTimeout = TimeSpan.FromMilliseconds(50) });

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken)
              .WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.Timeout, ex.ErrorCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task NonStreamingRequests_DoNotBufferErrorBodyBeforeReturning(string method)
    {
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
      {
        Content = new StreamContent(new NeverReadStream())
      });
      await using var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler, null);
      using var body = new StringContent("{}", Encoding.UTF8, "application/json");

      using var response = await (method == "GET"
          ? conn.GetAsync("/x", TestContext.Current.CancellationToken)
          : conn.PostAsync("/x", body, TestContext.Current.CancellationToken))
          .WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAsync_ResponseBodyRead_UsesRequestTimeout()
    {
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StreamContent(new NeverReadStream())
      });
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig { RequestTimeout = TimeSpan.FromMilliseconds(50) });

      using var response = await conn.GetAsync("/x", TestContext.Current.CancellationToken);

      // The wrapper's own TimeoutException (not the WaitAsync hang-guard) must surface,
      // proving ReadAsStringAsync routes through the timed serialize path.
      var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
          response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
              .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
      Assert.Contains("request timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_ResponseBodySyncRead_UsesRequestTimeout()
    {
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StreamContent(new NeverReadStream())
      });
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434"), handler, loggerFactory: null,
          new ModelApiConnectionConfig { RequestTimeout = TimeSpan.FromMilliseconds(50) });

      using var response = await conn.GetAsync("/x", TestContext.Current.CancellationToken);
      await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
      var buffer = new byte[1];

      var ex = Assert.Throws<TimeoutException>(() => stream.Read(buffer, 0, buffer.Length));
      Assert.Contains("request timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NeverHeadersHandler : HttpMessageHandler
    {
      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        return Json(HttpStatusCode.OK, "{}");
      }
    }

    private sealed class NeverReadStream : System.IO.Stream
    {
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        return 0;
      }

      public override void Flush()
      {
      }

      public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
      public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
  }
}
