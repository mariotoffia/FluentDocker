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
  }
}
