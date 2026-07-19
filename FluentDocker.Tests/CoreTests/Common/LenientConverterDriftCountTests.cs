using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// COMMON-3: the lenient int/string converters expose a process-global <c>DriftCount</c>
  /// (mirroring <see cref="TolerantDateTimeOffsetConverter.DriftCount"/>) that increments on a
  /// present-but-unparseable / structured-drift value, never on a JSON null or a clean parse.
  /// The positive tests assert a strict increase (monotonic, so robust to concurrent increments
  /// from the rest of the parallel suite). The clean/null tests assert only value-correctness: an
  /// exact "counter unchanged" assertion against a process-global static is unsound under xUnit
  /// parallelism (any other test deserializing a drifted label/list would move it), so the
  /// no-increment guarantee is instead enforced structurally — the counter is only touched inside
  /// the drift branches.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class LenientConverterDriftCountTests
  {
    private static readonly JsonSerializerOptions Int32Options =
        new() { Converters = { new LenientInt32Converter() } };
    private static readonly JsonSerializerOptions ListOptions =
        new() { Converters = { new LenientStringListConverter() } };
    private static readonly JsonSerializerOptions DictOptions =
        new() { Converters = { new LenientStringDictionaryConverter() } };

    [Fact]
    public void LenientInt32_DriftedValue_IncrementsDriftCount()
    {
      var before = LenientInt32Converter.DriftCount;

      var value = JsonSerializer.Deserialize<int>("\"not-a-number\"", Int32Options);

      Assert.Equal(0, value);
      Assert.True(LenientInt32Converter.DriftCount > before);
    }

    [Fact]
    public void LenientInt32_CleanAndNull_ParseWithoutError()
    {
      Assert.Equal(42, JsonSerializer.Deserialize<int>("42", Int32Options));
      Assert.Equal(0, JsonSerializer.Deserialize<int>("null", Int32Options));
    }

    [Fact]
    public void LenientStringList_DriftedValue_IncrementsDriftCount()
    {
      var before = LenientStringListConverter.DriftCount;

      // A bare number is neither an array, a null, nor a string: structured drift.
      Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<List<string>>("123", ListOptions));

      Assert.True(LenientStringListConverter.DriftCount > before);
    }

    [Fact]
    public void LenientStringList_Clean_ParsesWithoutError()
    {
      // (Top-level JSON null short-circuits in System.Text.Json to a null reference before the
      // converter runs, so it does not exercise this converter and is not asserted here.)
      Assert.Equal(new List<string> { "a", "b" }, JsonSerializer.Deserialize<List<string>>("[\"a\",\"b\"]", ListOptions));
    }

    [Fact]
    public void LenientStringDictionary_StructuredValue_IncrementsDriftCount()
    {
      var before = LenientStringDictionaryConverter.DriftCount;

      // A nested object where a scalar value was expected degrades to "" and is counted as drift.
      var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
          "{\"k\":{\"nested\":1}}", DictOptions);

      Assert.NotNull(dict);
      Assert.Equal(string.Empty, dict!["k"]);
      Assert.True(LenientStringDictionaryConverter.DriftCount > before);
    }

    [Fact]
    public void LenientStringDictionary_Clean_ParsesWithoutError()
    {
      // (Top-level JSON null short-circuits in System.Text.Json to a null reference before the
      // converter runs, so it does not exercise this converter and is not asserted here.)
      Assert.Equal(
          new Dictionary<string, string> { ["k"] = "v" },
          JsonSerializer.Deserialize<Dictionary<string, string>>("{\"k\":\"v\"}", DictOptions));
    }
  }
}
