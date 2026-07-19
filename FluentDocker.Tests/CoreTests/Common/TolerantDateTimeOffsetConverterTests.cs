using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Volumes;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public sealed class TolerantDateTimeOffsetConverterTests
  {
    [Fact]
    public void TryDeserialize_ContainerWithBadStartedAt_PreservesContainerAndDefaultsDate()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": "2021-13-45T99:99:99Z"
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(default, container.State!.StartedAt);
    }

    [Fact]
    public void TryDeserialize_VolumeWithBadCreatedAt_PreservesVolumeAndDefaultsDate()
    {
      var json = """
          [{
            "Name": "data",
            "CreatedAt": "not-a-date"
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

      Assert.True(ok);
      var volume = Assert.Single(volumes!);
      Assert.Equal("data", volume.Name);
      Assert.Equal(default, volume.Created);
    }

    [Fact]
    public void TryDeserialize_ContainerWithObjectAsStartedAt_PreservesContainerAndDefaultsDate()
    {
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": { "unexpected": "object" }
            }
          }]
          """;

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(default, container.State!.StartedAt);
    }

    [Fact]
    public void TryDeserialize_BadDate_IncrementsDriftCounterAndInvokesHook()
    {
      // MC-MAJ-1: a present-but-unparseable timestamp must be observable (counter + hook), not a
      // silent zero indistinguishable from a genuinely-unset date.
      string? captured = null;
      var before = TolerantDateTimeOffsetConverter.DriftCount;
      TolerantDateTimeOffsetConverter.OnDrift = raw => captured = raw;
      try
      {
        var json = """[{ "Name": "data", "CreatedAt": "not-a-date" }]""";
        Assert.True(JsonHelper.TryDeserialize<List<Volume>>(json, out _));
      }
      finally
      {
        TolerantDateTimeOffsetConverter.OnDrift = null;
      }

      Assert.True(TolerantDateTimeOffsetConverter.DriftCount > before);
      Assert.Equal("not-a-date", captured);
    }

    [Fact]
    public void TryDeserialize_ArbitraryUserType_IsNotSilentlyToleranced()
    {
      // MC-MAJ-1: the tolerant converter is scoped to FluentDocker.Model DTOs; an arbitrary user
      // type keeps strict parsing, so a bad date fails to deserialize instead of being zeroed.
      var json = """{ "When": "not-a-date" }""";

      var ok = JsonHelper.TryDeserialize<ExternalDto>(json, out _);

      Assert.False(ok);
    }

    [Fact]
    public void TryDeserialize_ContainerWithDockerNanosecondTimestamp_RoundsToHundredNanosecondTick()
    {
      // Docker emits 9 fractional digits (nanoseconds); .NET resolves to 100ns ticks. The
      // tolerant converter parses via DateTimeOffset.TryParse, which ROUNDS the sub-tick
      // remainder, so 0.123456789s resolves to .1234568 (1234568 ticks past the second).
      // This hardcoded expectation also differs from the strict built-in converter (which
      // truncates to .1234567), so it fails if the tolerant converter is removed.
      var json = """
          [{
            "Id": "abc123",
            "State": {
              "StartedAt": "2021-01-01T00:00:00.123456789Z"
            }
          }]
          """;

      var expected = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234568);

      var ok = JsonHelper.TryDeserialize<List<Container>>(json, out var containers);

      Assert.True(ok);
      var container = Assert.Single(containers!);
      Assert.Equal("abc123", container.Id);
      Assert.Equal(expected, container.State!.StartedAt);
    }

    // MDL-MAJ-4 / MC-MAJ-1: the tolerant handling must also cover DateTimeOffset? (the natural type
    // for timestamps Docker omits), or a single bad value would fail the whole payload under strict STJ.
    private static readonly JsonSerializerOptions NullableConverterOptions =
        new() { Converters = { new TolerantNullableDateTimeOffsetConverter() } };

    [Fact]
    public void TolerantNullableConverter_JsonNull_ReadsAsNull()
    {
      var result = JsonSerializer.Deserialize<DateTimeOffset?>("null", NullableConverterOptions);
      Assert.Null(result);
    }

    [Fact]
    public void TolerantNullableConverter_ValidTimestamp_Parses()
    {
      var result = JsonSerializer.Deserialize<DateTimeOffset?>("\"2020-01-02T03:04:05Z\"", NullableConverterOptions);
      Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), result);
    }

    [Fact]
    public void TolerantNullableConverter_UnparseableTimestamp_DriftsInsteadOfThrowing()
    {
      var result = JsonSerializer.Deserialize<DateTimeOffset?>("\"2021-13-45T99:99:99Z\"", NullableConverterOptions);

      // Present-but-unparseable drifts to the non-nullable default (year 0001), never throws.
      Assert.Equal(default(DateTimeOffset), result);
    }

    // CE-10: numeric epoch timestamps are valid daemon output, not drift.

    [Fact]
    public void TryDeserialize_NumericEpochSeconds_ParsesAsTimestampNotDrift()
    {
      // A parsed (non-default) value proves the number was read as a timestamp, not zeroed as
      // drift. (DriftCount is process-global, so no equality assertion — parallel tests mutate it.)
      var json = """[{ "Name": "data", "CreatedAt": 1720000000 }]""";

      var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

      Assert.True(ok);
      Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1720000000), Assert.Single(volumes!).Created);
    }

    [Fact]
    public void TryDeserialize_NumericEpochMilliseconds_ParsesViaMagnitudeHeuristic()
    {
      // Magnitudes above 10^12 cannot be epoch seconds (beyond year 9999) and are read as
      // epoch milliseconds (documented heuristic).
      var json = """[{ "Name": "data", "CreatedAt": 1720000000000 }]""";

      var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

      Assert.True(ok);
      Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1720000000000), Assert.Single(volumes!).Created);
    }

    [Fact]
    public void TryDeserialize_NumericBeyondTimestampRange_StillDriftsToDefault()
    {
      // Larger than FromUnixTimeMilliseconds' max — not representable, so it counts as drift.
      var before = TolerantDateTimeOffsetConverter.DriftCount;
      var json = """[{ "Name": "data", "CreatedAt": 999999999999999999 }]""";

      var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

      Assert.True(ok);
      Assert.Equal(default, Assert.Single(volumes!).Created);
      Assert.True(TolerantDateTimeOffsetConverter.DriftCount > before);
    }

    // ML-11: a throwing OnDrift hook must not poison the deserialization it observes.
    [Fact]
    public void TryDeserialize_ThrowingOnDriftHook_DoesNotPoisonDeserialization()
    {
      var before = TolerantDateTimeOffsetConverter.DriftCount;
      TolerantDateTimeOffsetConverter.OnDrift = _ => throw new InvalidOperationException("faulty hook");
      try
      {
        var json = """[{ "Name": "data", "CreatedAt": "not-a-date" }]""";

        var ok = JsonHelper.TryDeserialize<List<Volume>>(json, out var volumes);

        Assert.True(ok);
        Assert.Equal(default, Assert.Single(volumes!).Created);
        Assert.True(TolerantDateTimeOffsetConverter.DriftCount > before);
      }
      finally
      {
        TolerantDateTimeOffsetConverter.OnDrift = null;
      }
    }

    // A stand-in for an arbitrary consumer type (namespace is FluentDocker.Tests.*, not
    // FluentDocker.Model.*), so it is NOT touched by the tolerant DateTimeOffset modifier.
    private sealed class ExternalDto
    {
      public DateTimeOffset When { get; set; }
    }
  }
}
