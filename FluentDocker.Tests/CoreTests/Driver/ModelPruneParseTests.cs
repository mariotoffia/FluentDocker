using FluentDocker.Drivers.Docker.Cli.Components.Parsing;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for <see cref="ModelJsonParser.ParsePruneResult"/>: removed models and a
  /// reclaimed size are extracted when recognizable, and the raw output is always
  /// preserved so the result never silently reports "removed nothing / 0 bytes".
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelPruneParseTests
  {
    [Fact]
    public void ParsePruneResult_ExtractsRemovedAndReclaimed_AndPreservesRaw()
    {
      const string output =
          "Deleted: ai/smollm2:latest\n" +
          "Deleted: ai/qwen3:latest\n" +
          "Total reclaimed space: 1.5 GB\n";

      var result = ModelJsonParser.ParsePruneResult(output);

      Assert.Equal(2, result.Removed.Count);
      Assert.True(result.ReclaimedBytes > 1_000_000_000, $"expected >1GB, got {result.ReclaimedBytes}");
      Assert.Contains("reclaimed", result.RawOutput);
    }

    [Fact]
    public void ParsePruneResult_UnknownFormat_PreservesRawOutput_NoFabrication()
    {
      var result = ModelJsonParser.ParsePruneResult("some unrecognized prune output");

      Assert.Empty(result.Removed);
      Assert.Equal(0, result.ReclaimedBytes);
      Assert.Equal("some unrecognized prune output", result.RawOutput);
    }

    [Fact]
    public void ParsePruneResult_Null_IsSafe()
    {
      var result = ModelJsonParser.ParsePruneResult(null);

      Assert.Empty(result.Removed);
      Assert.Equal(0, result.ReclaimedBytes);
      Assert.Equal(string.Empty, result.RawOutput);
    }
  }
}
