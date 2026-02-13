using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiDriverBaseStreamHelpersTests
  {
    /// <summary>
    /// Test subclass to expose protected static methods for direct testing.
    /// </summary>
    private sealed class TestableDriverBase : DockerApiDriverBase
    {
      public TestableDriverBase() : base(new MockDockerApiConnection()) { }

      public static string TestStripHeaders(string raw) => StripDockerStreamHeaders(raw);

      public static Task<int> TestReadExact(
          Stream stream, byte[] buffer, int count, CancellationToken ct)
          => ReadExactAsync(stream, buffer, count, ct);
    }

    #region StripDockerStreamHeaders

    [Fact]
    public void StripDockerStreamHeaders_NullInput_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, TestableDriverBase.TestStripHeaders(null));
    }

    [Fact]
    public void StripDockerStreamHeaders_EmptyInput_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, TestableDriverBase.TestStripHeaders(""));
    }

    [Fact]
    public void StripDockerStreamHeaders_ShortInput_ReturnsRaw()
    {
      Assert.Equal("hello", TestableDriverBase.TestStripHeaders("hello"));
    }

    [Fact]
    public void StripDockerStreamHeaders_NoValidHeader_ReturnsRaw()
    {
      // First byte > 2 means not a valid Docker stream header
      var text = "This is plain text log output without any framing";
      Assert.Equal(text, TestableDriverBase.TestStripHeaders(text));
    }

    [Fact]
    public void StripDockerStreamHeaders_ValidStdoutFrame_StripsHeader()
    {
      var payload = "hello world";
      var frame = CreateFrame(1, payload);
      var raw = Encoding.UTF8.GetString(frame);

      var result = TestableDriverBase.TestStripHeaders(raw);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_ValidStderrFrame_StripsHeader()
    {
      var payload = "error output";
      var frame = CreateFrame(2, payload);
      var raw = Encoding.UTF8.GetString(frame);

      var result = TestableDriverBase.TestStripHeaders(raw);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_MultipleFrames_ConcatenatesPayloads()
    {
      var frame1 = CreateFrame(1, "first ");
      var frame2 = CreateFrame(1, "second");
      var combined = new byte[frame1.Length + frame2.Length];
      Array.Copy(frame1, 0, combined, 0, frame1.Length);
      Array.Copy(frame2, 0, combined, frame1.Length, frame2.Length);
      var raw = Encoding.UTF8.GetString(combined);

      var result = TestableDriverBase.TestStripHeaders(raw);

      Assert.Equal("first second", result);
    }

    [Fact]
    public void StripDockerStreamHeaders_MixedStdoutStderrFrames_ConcatenatesBoth()
    {
      var stdout = CreateFrame(1, "out ");
      var stderr = CreateFrame(2, "err");
      var combined = new byte[stdout.Length + stderr.Length];
      Array.Copy(stdout, 0, combined, 0, stdout.Length);
      Array.Copy(stderr, 0, combined, stdout.Length, stderr.Length);
      var raw = Encoding.UTF8.GetString(combined);

      var result = TestableDriverBase.TestStripHeaders(raw);

      Assert.Equal("out err", result);
    }

    #endregion

    #region ReadExactAsync

    [Fact]
    public async Task ReadExactAsync_ReadsExactBytes()
    {
      var data = Encoding.UTF8.GetBytes("hello world");
      using var stream = new MemoryStream(data);
      var buffer = new byte[5];

      var read = await TestableDriverBase.TestReadExact(
          stream, buffer, 5, CancellationToken.None);

      Assert.Equal(5, read);
      Assert.Equal("hello", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public async Task ReadExactAsync_StreamShorterThanCount_ReturnsActualBytesRead()
    {
      var data = Encoding.UTF8.GetBytes("hi");
      using var stream = new MemoryStream(data);
      var buffer = new byte[10];

      var read = await TestableDriverBase.TestReadExact(
          stream, buffer, 10, CancellationToken.None);

      Assert.Equal(2, read);
      Assert.Equal("hi", Encoding.UTF8.GetString(buffer, 0, read));
    }

    [Fact]
    public async Task ReadExactAsync_EmptyStream_ReturnsZero()
    {
      using var stream = new MemoryStream(Array.Empty<byte>());
      var buffer = new byte[8];

      var read = await TestableDriverBase.TestReadExact(
          stream, buffer, 8, CancellationToken.None);

      Assert.Equal(0, read);
    }

    [Fact]
    public async Task ReadExactAsync_HandlesPartialReads()
    {
      // Use a stream that delivers data in small chunks
      var data = Encoding.UTF8.GetBytes("abcdefghij");
      using var stream = new ChunkedStream(data, 3); // delivers 3 bytes at a time
      var buffer = new byte[10];

      var read = await TestableDriverBase.TestReadExact(
          stream, buffer, 10, CancellationToken.None);

      Assert.Equal(10, read);
      Assert.Equal("abcdefghij", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public async Task ReadExactAsync_CancellationToken_Respected()
    {
      var data = Encoding.UTF8.GetBytes("data");
      using var stream = new MemoryStream(data);
      var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
          await TestableDriverBase.TestReadExact(
              stream, new byte[4], 4, cts.Token));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a Docker multiplexed stream frame.
    /// Header: [stream_type:1][0:3][size:4 big-endian] followed by payload.
    /// </summary>
    private static byte[] CreateFrame(byte streamType, string payload)
    {
      var payloadBytes = Encoding.UTF8.GetBytes(payload);
      var frame = new byte[8 + payloadBytes.Length];
      frame[0] = streamType;
      frame[4] = (byte)((payloadBytes.Length >> 24) & 0xFF);
      frame[5] = (byte)((payloadBytes.Length >> 16) & 0xFF);
      frame[6] = (byte)((payloadBytes.Length >> 8) & 0xFF);
      frame[7] = (byte)(payloadBytes.Length & 0xFF);
      Array.Copy(payloadBytes, 0, frame, 8, payloadBytes.Length);
      return frame;
    }

    /// <summary>
    /// A stream that delivers data in fixed-size chunks to simulate partial reads.
    /// </summary>
    private sealed class ChunkedStream : Stream
    {
      private readonly byte[] _data;
      private readonly int _chunkSize;
      private int _position;

      public ChunkedStream(byte[] data, int chunkSize)
      {
        _data = data;
        _chunkSize = chunkSize;
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => _data.Length;
      public override long Position
      {
        get => _position;
        set => throw new NotSupportedException();
      }

      public override int Read(byte[] buffer, int offset, int count)
      {
        var available = Math.Min(count, Math.Min(_chunkSize, _data.Length - _position));
        if (available <= 0)
          return 0;
        Array.Copy(_data, _position, buffer, offset, available);
        _position += available;
        return available;
      }

      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
  }
}
