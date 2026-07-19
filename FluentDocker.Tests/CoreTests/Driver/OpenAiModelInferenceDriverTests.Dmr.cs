using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  public partial class OpenAiModelInferenceDriverTests
  {
    // ---- DMR-2: the /models success body must be size-capped exactly like the POST path
    // (ReadBoundedBodyAsync); an oversized body is a typed StreamParseError overflow, not a
    // silent full materialization. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListEngineModelsAsync_OversizedBody_ReturnsStreamParseError()
    {
      var driver = new OpenAiModelInferenceDriver(new OversizedModelsConnection(), ModelRunnerEndpoint.HostTcp());

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
      Assert.Contains("MiB", resp.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An <see cref="IModelApiConnection"/> whose <c>GET /models</c> returns a success response
    /// carrying ~65 MiB of lazily-produced filler — enough to push the driver's 64 MiB bounded
    /// read into overflow without the test ever allocating the whole body up front.
    /// </summary>
    private sealed class OversizedModelsConnection : IModelApiConnection
    {
      public Uri BaseAddress => new("http://localhost:12434");
      public TimeSpan? StreamFirstByteTimeout => null;
      public TimeSpan? StreamReadIdleTimeout => null;

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
          Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
          {
            Content = new StreamContent(new RepeatingStream(65L * 1024 * 1024))
          });

      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default) =>
          throw new NotSupportedException();
      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A read-only stream that yields a fixed number of filler bytes, then EOF.</summary>
    private sealed class RepeatingStream : Stream
    {
      private long _remaining;

      public RepeatingStream(long count) => _remaining = count;

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

      public override int Read(byte[] buffer, int offset, int count)
      {
        if (_remaining <= 0)
          return 0;
        var n = (int)Math.Min(count, _remaining);
        Array.Fill(buffer, (byte)'a', offset, n);
        _remaining -= n;
        return n;
      }

      public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
      {
        if (_remaining <= 0)
          return new ValueTask<int>(0);
        var n = (int)Math.Min(buffer.Length, _remaining);
        buffer.Span[..n].Fill((byte)'a');
        _remaining -= n;
        return new ValueTask<int>(n);
      }

      public override void Flush()
      {
      }

      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
  }
}
