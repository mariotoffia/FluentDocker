using System.Linq;
using FluentDocker.Drivers.Docker.Cli.Components.Parsing;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for <see cref="ModelJsonParser"/> over the real DMR v1.2.1
  /// fixtures (JSON + table fallbacks) plus malformed inputs.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelJsonParserTests
  {
    [Fact]
    public void ParseList_RealLsJson()
    {
      var models = ModelJsonParser.ParseList(DmrFixtures.Load("ls.json"));

      Assert.NotEmpty(models);
      var smollm2 = models.First(m => m.Reference.Name == "smollm2");
      Assert.Equal("gguf", smollm2.Format);
      Assert.Equal("llama", smollm2.Architecture);
      Assert.Equal("361.82 M", smollm2.ParameterCount);
      Assert.Equal("IQ2_XXS/Q4_K_M", smollm2.Quantization);
      Assert.StartsWith("sha256:", smollm2.Id);
      Assert.True(smollm2.Size > 0, "size should be parsed from the human-readable string");
      Assert.Contains("docker.io/ai/smollm2:latest", smollm2.Tags);
    }

    [Fact]
    public void ParseInfo_RealInspectJson()
    {
      var info = ModelJsonParser.ParseInfo(DmrFixtures.Load("inspect.json"));

      Assert.NotNull(info);
      Assert.Equal("smollm2", info.Reference.Name);
      Assert.Equal("gguf", info.Format);
      Assert.True(info.Created.Year >= 2024);
    }

    [Fact]
    public void ParsePsTable_RealPsOutput()
    {
      var running = ModelJsonParser.ParsePsTable(DmrFixtures.Load("ps.txt"));

      Assert.NotEmpty(running);
      var first = running[0];
      Assert.Equal("smollm2", first.Reference.Name);
      Assert.Equal("llama.cpp", first.Backend);
      Assert.Contains(first.Mode, new[] { "completion", "embedding" });
    }

    [Fact]
    public void ParseDfTable_RealDfOutput()
    {
      var df = ModelJsonParser.ParseDfTable(DmrFixtures.Load("df.txt"));
      Assert.True(df.ModelsSizeBytes > 0);
    }

    [Fact]
    public void ParseLsTable_RealLsTableOutput()
    {
      var models = ModelJsonParser.ParseLsTable(DmrFixtures.Load("ls.txt"));

      Assert.NotEmpty(models);
      Assert.Contains(models, m => m.Reference.Name == "smollm2");
    }

    [Fact]
    public void ParseVersion_ParsesClientVersion()
    {
      const string text = "Client:\n Version:    v1.2.1\n OS/Arch:    darwin/arm64\n\nServer:\n Version:    v1.2.1\n Engine:     Docker Desktop\n";
      var v = ModelJsonParser.ParseVersion(text);

      Assert.Equal("v1.2.1", v.CliVersion);
      Assert.Contains("Docker Desktop", v.EngineVersion);
    }

    [Theory]
    [InlineData("Downloaded 232.61MB of 270.60MB", 232_610_000L, 270_600_000L)]
    public void ParsePullLine_Progress(string line, long minCurrent, long minTotal)
    {
      var p = ModelJsonParser.ParsePullLine(line);

      Assert.NotNull(p);
      Assert.True(p.Current >= minCurrent * 9 / 10);
      Assert.True(p.Total >= minTotal * 9 / 10);
      Assert.True(p.Fraction > 0 && p.Fraction <= 1);
    }

    [Fact]
    public void ParsePullLine_Completion()
    {
      var p = ModelJsonParser.ParsePullLine("Model pulled successfully");
      Assert.NotNull(p);
      Assert.Contains("success", p.Status, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    public void ParseList_Malformed_ReturnsEmpty(string input)
    {
      Assert.Empty(ModelJsonParser.ParseList(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseList_EmptyOrWhitespace_IsGenuinelyEmpty(string input)
    {
      // No output (the runner has no models) is a successful empty result.
      Assert.True(ModelJsonParser.TryParseList(input, out var models));
      Assert.Empty(models);
    }

    [Fact]
    public void TryParseList_ValidArray_ReturnsTrueWithItems()
    {
      Assert.True(ModelJsonParser.TryParseList(DmrFixtures.Load("ls.json"), out var models));
      Assert.NotEmpty(models);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ broken")]
    [InlineData("{\"message\":\"error\"}")] // valid JSON, but an object, not the expected array
    public void TryParseList_MalformedNonEmpty_ReturnsFalse(string input)
    {
      // A non-empty payload that is not a JSON array is a parse FAILURE — it must not be
      // silently reported as "zero models".
      Assert.False(ModelJsonParser.TryParseList(input, out var models));
      Assert.Empty(models);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseInfo_Malformed_ReturnsNull(string input)
    {
      Assert.Null(ModelJsonParser.ParseInfo(input));
    }

    [Fact]
    public void ParseList_NonStringId_DoesNotThrow()
    {
      // a numeric id must not make GetString() throw out of the exception-safe parser
      const string json = "[{\"id\":12345,\"tags\":[\"ai/x:latest\"],\"created\":1742816981,\"config\":{\"format\":\"gguf\"}}]";
      var models = ModelJsonParser.ParseList(json);

      Assert.Single(models);
      Assert.Equal("x", models[0].Reference.Name);
      Assert.Null(models[0].Id);
    }

    [Fact]
    public void ParseNativePullProgress_NonStringFields_DoNotThrow()
    {
      const string line = "{\"type\":42,\"total\":100,\"layer\":{\"current\":50}}";
      var p = ModelJsonParser.ParseNativePullProgress(line);

      Assert.NotNull(p);
      Assert.Equal(50, p.Current);
      Assert.Equal(100, p.Total);
    }

    // ---- M3: an oversized (pathological) payload is rejected without unbounded parsing ----

    // Just over the parser's 8 MiB char cap. Built as syntactically valid JSON so the guard
    // (not a JsonException) is what rejects it.
    private static string OversizedJson(char open, char close)
    {
      const int overCap = (8 * 1024 * 1024) + 16;
      var sb = new System.Text.StringBuilder(overCap + 2);
      sb.Append(open);
      sb.Append('"').Append(new string('a', overCap)).Append('"');
      sb.Append(close);
      return sb.ToString();
    }

    [Fact]
    public void TryParseList_OversizedInput_ReturnsFailure_NotEmptyOk()
    {
      // An oversized array payload must be a parse FAILURE (false), not a silent "zero models",
      // and must not be fully parsed/cloned.
      Assert.False(ModelJsonParser.TryParseList(OversizedJson('[', ']'), out var models));
      Assert.Empty(models);
    }

    [Fact]
    public void ParseList_OversizedInput_ReturnsEmpty_NoThrow()
    {
      Assert.Empty(ModelJsonParser.ParseList(OversizedJson('[', ']')));
    }

    [Fact]
    public void ParseInfo_OversizedInput_ReturnsNull_NoThrow()
    {
      Assert.Null(ModelJsonParser.ParseInfo(OversizedJson('{', '}')));
    }

    [Fact]
    public void ParseNativePullProgress_OversizedInput_ReturnsNull_NoThrow()
    {
      Assert.Null(ModelJsonParser.ParseNativePullProgress(OversizedJson('{', '}')));
    }

    // ============================ M20: size-unit variants ============================
    // ParseSize is private, so it is exercised through ParsePullLine, whose "X of Y"
    // pattern feeds both operands through ParseSize. This covers the decimal (MB/GB/kB,
    // 1000-based) vs binary (MiB/GiB/KiB, 1024-based) distinction and the optional space
    // between the number and the unit.

    [Theory]
    // decimal units (base 1000)
    [InlineData("Downloaded 1MB of 2MB", 1_000_000L, 2_000_000L)]
    [InlineData("Downloaded 1.5GB of 3GB", 1_500_000_000L, 3_000_000_000L)]
    [InlineData("Downloaded 103.56kB of 200kB", 103_560L, 200_000L)]
    // binary units (base 1024) — same magnitude letter is LARGER than the decimal form
    [InlineData("Downloaded 1MiB of 2MiB", 1_048_576L, 2_097_152L)]
    [InlineData("Downloaded 1GiB of 2GiB", 1_073_741_824L, 2_147_483_648L)]
    // optional space between number and unit must parse identically
    [InlineData("Downloaded 256.35 MiB of 256.35 MiB", 268_802_457L, 268_802_457L)]
    public void ParsePullLine_SizeUnits_DecimalVsBinary(string line, long expectedCurrent, long expectedTotal)
    {
      var p = ModelJsonParser.ParsePullLine(line);

      Assert.NotNull(p);
      Assert.Equal("Downloading", p.Status);
      Assert.Equal(expectedCurrent, p.Current);
      Assert.Equal(expectedTotal, p.Total);
    }

    [Fact]
    public void ParsePullLine_BinaryIsLargerThanDecimal_ForSameLetter()
    {
      // 1 MiB (1,048,576) must exceed 1 MB (1,000,000) — proves the 'i' binary flag is honored.
      var binary = ModelJsonParser.ParsePullLine("Downloaded 1MiB of 1MiB");
      var dec = ModelJsonParser.ParsePullLine("Downloaded 1MB of 1MB");
      Assert.True(binary.Current > dec.Current);
    }

    // ============================ M20: locale invariance ============================
    // ParseSize uses double.TryParse with CultureInfo.InvariantCulture, so a '.' is ALWAYS
    // the decimal separator regardless of the ambient thread culture. We run the assertions
    // under a comma-decimal culture (de-DE) to prove the parser is not culture-sensitive.

    [Fact]
    public void ParseSize_UsesInvariantCulture_NotThreadCulture()
    {
      var original = System.Threading.Thread.CurrentThread.CurrentCulture;
      try
      {
        System.Threading.Thread.CurrentThread.CurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("de-DE");

        // The '.' decimal point is parsed via InvariantCulture even under a comma-decimal
        // ambient culture: "1.5GB" stays 1.5 GB == 1,500,000,000 bytes (a culture-sensitive
        // parser would treat the '.' as a thousands separator and get 15 GB).
        var dotDecimal = ModelJsonParser.ParsePullLine("Downloaded 1.5GB of 3GB");
        Assert.Equal(1_500_000_000L, dotDecimal.Current);

        // A comma is NEVER treated as a decimal point: "1,5GB" is not parsed as 1.5 GB
        // (1,500,000,000). The size grammar has no comma/locale handling, so the comma simply
        // is not interpreted as a decimal separator regardless of the de-DE culture.
        var commaDecimal = ModelJsonParser.ParsePullLine("Downloaded 1,5GB of 3GB");
        Assert.NotNull(commaDecimal);
        Assert.NotEqual(1_500_000_000L, commaDecimal.Current);
      }
      finally
      {
        System.Threading.Thread.CurrentThread.CurrentCulture = original;
      }
    }

    // ====================== M20: --json shape vs table fallback ======================
    // ps/df have no `--json` in DMR v1.2.1 (table only); ls has both. The same logical
    // data must come out of the JSON path and the table path.

    [Fact]
    public void ParseLs_JsonAndTable_AgreeOnSmollm2()
    {
      var fromJson = ModelJsonParser.ParseList(DmrFixtures.Load("ls.json"))
          .First(m => m.Reference.Name == "smollm2");
      var fromTable = ModelJsonParser.ParseLsTable(DmrFixtures.Load("ls.txt"))
          .First(m => m.Reference.Name == "smollm2");

      // Both surfaces resolve the same architecture/quantization and a positive size.
      Assert.Equal(fromJson.Architecture, fromTable.Architecture);
      Assert.Equal(fromJson.Quantization, fromTable.Quantization);
      Assert.True(fromTable.Size > 0);
    }

    [Theory]
    [InlineData("ps.txt")]   // ps: table only (no --json in DMR v1.2.1)
    public void ParsePsTable_TableFallback_ParsesRunningFields(string fixture)
    {
      var running = ModelJsonParser.ParsePsTable(DmrFixtures.Load(fixture));

      var smollm2 = running.First(r => r.Reference.Name == "smollm2");
      Assert.Equal("llama.cpp", smollm2.Backend);
      Assert.Equal("completion", smollm2.Mode);
    }

    [Fact]
    public void ParseDfTable_DecimalUnits_ConvertCorrectly()
    {
      // df.txt reports "Models  1.20GB" (decimal GB == 1,200,000,000 bytes).
      var df = ModelJsonParser.ParseDfTable(DmrFixtures.Load("df.txt"));
      Assert.Equal(1_200_000_000L, df.ModelsSizeBytes);
    }

    // ====================== M20: malformed input is exception-safe ======================

    [Theory]
    [InlineData("Downloaded of")]                  // "X of Y" with no sizes -> status-only event
    [InlineData("preparing layers...")]            // free-form status line, no sizes
    [InlineData("Downloaded ??? of ???")]          // size-like position but non-numeric tokens
    public void ParsePullLine_Malformed_ReturnsStatusEvent_NoThrow(string line)
    {
      var p = ModelJsonParser.ParsePullLine(line);
      Assert.NotNull(p);
      Assert.Equal(0L, p.Current);
      Assert.Equal(0L, p.Total);
      Assert.Equal(line.Trim(), p.Status); // the raw line is preserved as the status
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParsePullLine_EmptyOrWhitespace_ReturnsNull(string line)
    {
      Assert.Null(ModelJsonParser.ParsePullLine(line));
    }
  }
}
