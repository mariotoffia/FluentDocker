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
    private sealed class TestableDriverBase : DockerApiDriverBase
    {
      public TestableDriverBase() : base(new MockDockerApiConnection()) { }

      public static string TestStripHeaders(byte[] bytes) => StripDockerStreamHeaders(bytes);

      public static Task<int> TestReadExact(
          Stream stream, byte[] buffer, int count, CancellationToken ct)
          => ReadExactAsync(stream, buffer, count, ct);
    }

    #region StripDockerStreamHeaders — Basic

    [Fact]
    public void StripDockerStreamHeaders_NullInput_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, TestableDriverBase.TestStripHeaders(null));
    }

    [Fact]
    public void StripDockerStreamHeaders_EmptyInput_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, TestableDriverBase.TestStripHeaders([]));
    }

    [Fact]
    public void StripDockerStreamHeaders_ShortInput_ReturnsRaw()
    {
      var bytes = Encoding.UTF8.GetBytes("hello");
      Assert.Equal("hello", TestableDriverBase.TestStripHeaders(bytes));
    }

    [Fact]
    public void StripDockerStreamHeaders_NoValidHeader_ReturnsRaw()
    {
      var text = "This is plain text log output without any framing";
      var bytes = Encoding.UTF8.GetBytes(text);
      Assert.Equal(text, TestableDriverBase.TestStripHeaders(bytes));
    }

    [Fact]
    public void StripDockerStreamHeaders_ValidStdoutFrame_StripsHeader()
    {
      var payload = "hello world";
      var frame = CreateFrame(1, payload);

      var result = TestableDriverBase.TestStripHeaders(frame);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_ValidStderrFrame_StripsHeader()
    {
      var payload = "error output";
      var frame = CreateFrame(2, payload);

      var result = TestableDriverBase.TestStripHeaders(frame);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_MultipleFrames_ConcatenatesPayloads()
    {
      var frame1 = CreateFrame(1, "first ");
      var frame2 = CreateFrame(1, "second");
      var combined = CombineFrames(frame1, frame2);

      var result = TestableDriverBase.TestStripHeaders(combined);

      Assert.Equal("first second", result);
    }

    [Fact]
    public void StripDockerStreamHeaders_MixedStdoutStderrFrames_ConcatenatesBoth()
    {
      var stdout = CreateFrame(1, "out ");
      var stderr = CreateFrame(2, "err");
      var combined = CombineFrames(stdout, stderr);

      var result = TestableDriverBase.TestStripHeaders(combined);

      Assert.Equal("out err", result);
    }

    #endregion

    #region StripDockerStreamHeaders — Non-ASCII (UTF-8 correctness)

    [Fact]
    public void StripDockerStreamHeaders_CjkCharacters_RoundTripsCorrectly()
    {
      // CJK characters are 3-byte UTF-8 sequences
      var payload = "Hello 世界";
      var frame = CreateFrame(1, payload);

      var result = TestableDriverBase.TestStripHeaders(frame);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_Emoji_RoundTripsCorrectly()
    {
      // Emoji are 4-byte UTF-8 sequences
      var payload = "\U0001F680 rocket \U0001F4A5 boom";
      var frame = CreateFrame(1, payload);

      var result = TestableDriverBase.TestStripHeaders(frame);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_AccentedCharacters_RoundTripsCorrectly()
    {
      var payload = "caf\u00e9 na\u00efve r\u00e9sum\u00e9";
      var frame = CreateFrame(1, payload);

      var result = TestableDriverBase.TestStripHeaders(frame);

      Assert.Equal(payload, result);
    }

    [Fact]
    public void StripDockerStreamHeaders_MultiFrameNonAscii_FrameBoundaryCorrect()
    {
      // Frame 1 ends with a CJK character, frame 2 starts with an emoji
      var frame1 = CreateFrame(1, "Hello 世界");
      var frame2 = CreateFrame(2, "\U0001F680 rocket");
      var combined = CombineFrames(frame1, frame2);

      var result = TestableDriverBase.TestStripHeaders(combined);

      Assert.Equal("Hello 世界\U0001F680 rocket", result);
    }

    [Fact]
    public void StripDockerStreamHeaders_LargeNonAsciiPayload_ExceedsStackallocThreshold()
    {
      // Each CJK char is 3 bytes in UTF-8. 200 chars × 3 = 600 bytes per frame.
      // Two frames = 1200 bytes total payload, exceeding the 1024-byte stackalloc threshold.
      var cjkChars = new string('\u4e16', 200); // 200 × '世'
      var frame1 = CreateFrame(1, cjkChars);
      var frame2 = CreateFrame(1, cjkChars);
      var combined = CombineFrames(frame1, frame2);

      var result = TestableDriverBase.TestStripHeaders(combined);

      Assert.Equal(cjkChars + cjkChars, result);
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
      using var stream = new MemoryStream([]);
      var buffer = new byte[8];

      var read = await TestableDriverBase.TestReadExact(
          stream, buffer, 8, CancellationToken.None);

      Assert.Equal(0, read);
    }

    [Fact]
    public async Task ReadExactAsync_HandlesPartialReads()
    {
      var data = Encoding.UTF8.GetBytes("abcdefghij");
      using var stream = new ChunkedStream(data, 3);
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

    private static byte[] CombineFrames(byte[] a, byte[] b)
    {
      var combined = new byte[a.Length + b.Length];
      Array.Copy(a, 0, combined, 0, a.Length);
      Array.Copy(b, 0, combined, a.Length, b.Length);
      return combined;
    }

    private sealed class ChunkedStream(byte[] data, int chunkSize) : Stream
    {
      private readonly byte[] _data = data;
      private readonly int _chunkSize = chunkSize;
      private int _position;

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
