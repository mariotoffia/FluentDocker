using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="ModelApiConnectionConfig"/> configuration properties
  /// and <see cref="ModelApiConnection.ReadWithIdleTimeoutAsync"/> idle-timeout behaviour.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelApiConnectionConfigTests
  {
    private sealed class StalledStream : Stream
    {
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => 0;
      public override long Position { get => 0; set { } }
      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => 0;
      public override void SetLength(long value) { }
      public override void Write(byte[] buffer, int offset, int count) { }
      public override int Read(byte[] buffer, int offset, int count) => 0;
      public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
      {
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
      }
      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
      {
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
      }
    }

    [Fact]
    public void StreamReadIdleTimeout_DefaultIsNull()
    {
      var config = new ModelApiConnectionConfig();
      Assert.Null(config.StreamReadIdleTimeout);
    }

    [Fact]
    public void StreamReadIdleTimeout_CanBeSet()
    {
      var config = new ModelApiConnectionConfig { StreamReadIdleTimeout = TimeSpan.FromSeconds(30) };
      Assert.Equal(TimeSpan.FromSeconds(30), config.StreamReadIdleTimeout);
    }

    [Fact]
    public void AllowTlsHostnameMismatch_DefaultIsFalse()
    {
      var config = new ModelApiConnectionConfig();
      Assert.False(config.AllowTlsHostnameMismatch);
    }

    [Fact]
    public void AllowTlsHostnameMismatch_CanBeSet()
    {
      var config = new ModelApiConnectionConfig { AllowTlsHostnameMismatch = true };
      Assert.True(config.AllowTlsHostnameMismatch);
    }

    [Fact]
    public async Task ReadWithIdleTimeout_StallsStream_ThrowsModelRunnerException()
    {
      using var handler = new HttpClientHandler();
      var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler,
          requestTimeout: TimeSpan.FromSeconds(10));

      // We need a connection with the idle timeout configured, but the ctor that takes
      // config doesn't expose it yet via this overload. Create via endpoint ctor.
      var config = new ModelApiConnectionConfig { StreamReadIdleTimeout = TimeSpan.FromMilliseconds(100) };
      await using var connWithTimeout = new ModelApiConnection(
          ModelRunnerEndpoint.HostTcp(), config);

      var stalled = new StalledStream();
      var buffer = new byte[64].AsMemory();

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
          await connWithTimeout.ReadWithIdleTimeoutAsync(stalled, buffer, CancellationToken.None));
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task ReadWithIdleTimeout_NullTimeout_HonorsCancellation()
    {
      // With no idle timeout set, cancellation still terminates the stalled read.
      await using var conn = new ModelApiConnection(ModelRunnerEndpoint.HostTcp());
      var stalled = new StalledStream();
      var buffer = new byte[64].AsMemory();
      using var cts = new CancellationTokenSource(100);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
          await conn.ReadWithIdleTimeoutAsync(stalled, buffer, cts.Token));
    }
  }
}
