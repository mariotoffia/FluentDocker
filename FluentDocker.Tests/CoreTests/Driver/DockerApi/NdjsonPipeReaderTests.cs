using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  // ═══════════════════════════════════════════════════════════════
  // Phase E: UTF-8 NDJSON PipeReader Tests
  //
  // Validates that ReadNdjsonLinesAsync<T> correctly:
  //   Stream → PipeReader → UTF-8 line scan → Deserialize<T>
  // without intermediate string allocation per line.
  //
  // Data Flow:
  //   MemoryStream ──▶ PipeReader ──▶ line scan (\n) ──▶ Utf8JsonReader ──▶ T
  // ═══════════════════════════════════════════════════════════════

  /// <summary>
  /// Simple model for NDJSON deserialization tests.
  /// </summary>
  internal sealed class NdjsonTestItem
  {
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; }
  }

  [JsonSerializable(typeof(NdjsonTestItem))]
  internal sealed partial class NdjsonTestJsonContext : JsonSerializerContext { }

  [Trait("Category", "Unit")]
  public class NdjsonPipeReaderTests
  {
    /// <summary>
    /// Testable wrapper that exposes the protected static ReadNdjsonLinesAsync.
    /// </summary>
    private sealed class TestableDriverBase : DockerApiDriverBase
    {
      public TestableDriverBase() : base(new MockDockerApiConnection()) { }

      public static IAsyncEnumerable<T> TestReadNdjsonLines<T>(
          Stream stream, JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
          => ReadNdjsonLinesAsync(stream, typeInfo, ct);
    }

    private static JsonTypeInfo<NdjsonTestItem> TypeInfo =>
        NdjsonTestJsonContext.Default.NdjsonTestItem;

    #region Basic Behavior

    /// <summary>
    /// Validates that an empty stream produces zero items.
    /// </summary>
    [Fact]
    public async Task EmptyStream_YieldsNothing()
    {
      using var stream = MakeStream("");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Empty(items);
    }

    /// <summary>
    /// Validates that a single JSON line is deserialized to one typed object.
    /// </summary>
    [Fact]
    public async Task SingleLine_DeserializesCorrectly()
    {
      using var stream = MakeStream("{\"id\":\"a\",\"value\":1}\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Single(items);
      Assert.Equal("a", items[0].Id);
      Assert.Equal(1, items[0].Value);
    }

    /// <summary>
    /// Validates that multiple JSON lines yield all objects in order.
    /// </summary>
    [Fact]
    public async Task MultipleLines_YieldsAllInOrder()
    {
      using var stream = MakeStream(
          "{\"id\":\"a\",\"value\":1}\n" +
          "{\"id\":\"b\",\"value\":2}\n" +
          "{\"id\":\"c\",\"value\":3}\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(3, items.Count);
      Assert.Equal("a", items[0].Id);
      Assert.Equal("b", items[1].Id);
      Assert.Equal("c", items[2].Id);
    }

    #endregion

    #region Line Skipping

    /// <summary>
    /// Validates that empty lines between JSON objects are silently skipped.
    /// </summary>
    [Fact]
    public async Task EmptyLinesBetweenJson_Skipped()
    {
      using var stream = MakeStream(
          "{\"id\":\"a\",\"value\":1}\n\n\n{\"id\":\"b\",\"value\":2}\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(2, items.Count);
      Assert.Equal("a", items[0].Id);
      Assert.Equal("b", items[1].Id);
    }

    /// <summary>
    /// Validates that lines containing only whitespace are skipped.
    /// </summary>
    [Fact]
    public async Task WhitespaceOnlyLines_Skipped()
    {
      using var stream = MakeStream(
          "   \n\t\n{\"id\":\"a\",\"value\":1}\n  \t \n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Single(items);
      Assert.Equal("a", items[0].Id);
    }

    /// <summary>
    /// Validates that an invalid JSON line is silently skipped without throwing.
    /// </summary>
    [Fact]
    public async Task InvalidJsonLine_Skipped()
    {
      using var stream = MakeStream("not-json\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Empty(items);
    }

    /// <summary>
    /// Validates that only valid items are yielded when mixed with invalid JSON.
    /// </summary>
    [Fact]
    public async Task MixedValidInvalid_YieldsOnlyValid()
    {
      using var stream = MakeStream(
          "{\"id\":\"a\",\"value\":1}\n" +
          "BROKEN\n" +
          "{invalid json}\n" +
          "{\"id\":\"b\",\"value\":2}\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(2, items.Count);
      Assert.Equal("a", items[0].Id);
      Assert.Equal("b", items[1].Id);
    }

    #endregion

    #region UTF-8 Correctness

    /// <summary>
    /// Validates that non-ASCII UTF-8 content (CJK, emoji) round-trips correctly.
    ///
    /// UTF-8 encoding:
    ///   CJK '世' = 3 bytes (E4 B8 96)
    ///   Emoji '🚀' = 4 bytes (F0 9F 9A 80)
    /// </summary>
    [Fact]
    public async Task NonAsciiUtf8_DeserializedCorrectly()
    {
      using var stream = MakeStream(
          "{\"id\":\"世界\",\"value\":42,\"text\":\"🚀 launch\"}\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Single(items);
      Assert.Equal("世界", items[0].Id);
      Assert.Equal("🚀 launch", items[0].Text);
    }

    #endregion

    #region Buffer Boundaries

    /// <summary>
    /// Validates that a line larger than the default PipeReader buffer (4KB)
    /// is correctly assembled across multiple read operations.
    /// </summary>
    [Fact]
    public async Task LargeLine_SpansBufferBoundary()
    {
      // Create a JSON line with a ~6KB text value to exceed 4KB buffer
      var largeText = new string('x', 6000);
      var json = $"{{\"id\":\"big\",\"value\":0,\"text\":\"{largeText}\"}}\n";
      using var stream = MakeStream(json);

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Single(items);
      Assert.Equal("big", items[0].Id);
      Assert.Equal(largeText, items[0].Text);
    }

    /// <summary>
    /// Validates that the last line is parsed even without a trailing newline.
    /// Docker NDJSON streams may not always end with \n.
    /// </summary>
    [Fact]
    public async Task NoTrailingNewline_LastLineParsed()
    {
      using var stream = MakeStream("{\"id\":\"last\",\"value\":99}");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Single(items);
      Assert.Equal("last", items[0].Id);
      Assert.Equal(99, items[0].Value);
    }

    /// <summary>
    /// Validates that multiple complete lines followed by a final line without \n
    /// are all correctly parsed — the last fragment is handled after IsCompleted.
    /// </summary>
    [Fact]
    public async Task MultipleLinesPlusNoTrailingNewline_AllParsed()
    {
      using var stream = MakeStream(
          "{\"id\":\"a\",\"value\":1}\n{\"id\":\"b\",\"value\":2}\n{\"id\":\"c\",\"value\":3}");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(3, items.Count);
      Assert.Equal("a", items[0].Id);
      Assert.Equal("b", items[1].Id);
      Assert.Equal("c", items[2].Id);
    }

    /// <summary>
    /// Validates that \r\n (Windows-style) line endings work correctly.
    /// </summary>
    [Fact]
    public async Task CrLfLineEndings_HandledCorrectly()
    {
      using var stream = MakeStream(
          "{\"id\":\"a\",\"value\":1}\r\n{\"id\":\"b\",\"value\":2}\r\n");

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(2, items.Count);
      Assert.Equal("a", items[0].Id);
      Assert.Equal("b", items[1].Id);
    }

    /// <summary>
    /// Validates correct line assembly when the stream delivers data in small chunks,
    /// simulating network fragmentation where a single JSON line arrives in pieces.
    ///
    /// Delivery pattern (3-byte chunks):
    ///   {"i ──▶ d": ──▶ "a" ──▶ ,"v ──▶ alu ──▶ ...
    /// </summary>
    [Fact]
    public async Task ChunkedDelivery_AssemblesLines()
    {
      var json = "{\"id\":\"chunked\",\"value\":7}\n{\"id\":\"also\",\"value\":8}\n";
      var bytes = Encoding.UTF8.GetBytes(json);
      using var stream = new ChunkedStream(bytes, chunkSize: 3);

      var items = await CollectAsync(
          TestableDriverBase.TestReadNdjsonLines(stream, TypeInfo, CancellationToken.None));

      Assert.Equal(2, items.Count);
      Assert.Equal("chunked", items[0].Id);
      Assert.Equal("also", items[1].Id);
    }

    #endregion

    #region Cancellation

    /// <summary>
    /// Validates that cancellation stops enumeration promptly.
    /// </summary>
    [Fact]
    public async Task Cancellation_StopsReading()
    {
      var cts = new CancellationTokenSource();
      // Large stream with many lines — cancel after first item
      var sb = new StringBuilder();
      for (var i = 0; i < 1000; i++)
        sb.Append($"{{\"id\":\"{i}\",\"value\":{i}}}\n");
      using var stream = MakeStream(sb.ToString());

      var items = new List<NdjsonTestItem>();
      await foreach (var item in TestableDriverBase.TestReadNdjsonLines(
          stream, TypeInfo, cts.Token))
      {
        items.Add(item);
        if (items.Count == 1)
          cts.Cancel();
      }

      // Cancel fires after item 1; inner loop checks ct per-line, so at most 1 extra
      Assert.InRange(items.Count, 1, 2);
    }

    #endregion

    #region Helpers

    private static MemoryStream MakeStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
      var list = new List<T>();
      await foreach (var item in source)
        list.Add(item);
      return list;
    }

    /// <summary>
    /// Stream that delivers data in fixed-size chunks to simulate network fragmentation.
    /// </summary>
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

      public override ValueTask<int> ReadAsync(
          Memory<byte> buffer, CancellationToken ct = default)
      {
        var available = Math.Min(buffer.Length, Math.Min(_chunkSize, _data.Length - _position));
        if (available <= 0)
          return new ValueTask<int>(0);
        _data.AsMemory(_position, available).CopyTo(buffer);
        _position += available;
        return new ValueTask<int>(available);
      }

      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) =>
          throw new NotSupportedException();
    }

    #endregion
  }
}
