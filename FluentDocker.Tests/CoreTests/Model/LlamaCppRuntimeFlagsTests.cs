using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// Unit tests for <see cref="LlamaCppRuntimeFlags"/>: flag rendering, range
  /// validation, boolean-flag semantics and raw passthrough.
  /// </summary>
  [Trait("Category", "Unit")]
  public class LlamaCppRuntimeFlagsTests
  {
    [Fact]
    public void ToArgs_RendersSamplingFlags()
    {
      var args = new LlamaCppRuntimeFlags { Temperature = 0.7, TopK = 40, TopP = 0.9 }.ToArgs();

      Assert.Equal(new[] { "--temp", "0.7", "--top-k", "40", "--top-p", "0.9" }, args);
    }

    [Fact]
    public void ToArgs_FormatsDoublesInvariant()
    {
      var args = new LlamaCppRuntimeFlags { MinP = 0.05 }.ToArgs();
      Assert.Equal(new[] { "--min-p", "0.05" }, args);
    }

    [Fact]
    public void ToArgs_BooleanTrue_EmitsBareFlag()
    {
      var args = new LlamaCppRuntimeFlags { Mlock = true, NoMmap = true, NoPrefillAssistant = true }.ToArgs();

      Assert.Contains("--mlock", args);
      Assert.Contains("--no-mmap", args);
      Assert.Contains("--no-prefill-assistant", args);
    }

    [Fact]
    public void ToArgs_BooleanFalse_Omitted()
    {
      var args = new LlamaCppRuntimeFlags { Mlock = false, NoMmap = false }.ToArgs();
      Assert.Empty(args);
    }

    [Fact]
    public void ToArgs_RendersPerformanceAndGpuAndRope()
    {
      var args = new LlamaCppRuntimeFlags
      {
        Threads = 8,
        BatchSize = 512,
        GpuLayers = 35,
        MainGpu = 0,
        SplitMode = "layer",
        RopeFreqBase = 10000.0,
        ReasoningBudget = 0
      }.ToArgs();

      Assert.Contains("--threads", args);
      Assert.Contains("8", args);
      Assert.Contains("--n-gpu-layers", args);
      Assert.Contains("35", args);
      Assert.Contains("--split-mode", args);
      Assert.Contains("layer", args);
      Assert.Contains("--rope-freq-base", args);
    }

    [Fact]
    public void ToArgs_AppendsRawVerbatim()
    {
      var args = new LlamaCppRuntimeFlags
      {
        Temperature = 0.5,
        Raw = new[] { "--custom-flag", "x" }
      }.ToArgs();

      Assert.Equal("--custom-flag", args[args.Count - 2]);
      Assert.Equal("x", args[args.Count - 1]);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void ToArgs_TemperatureOutOfRange_Throws(double temp)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { Temperature = temp }.ToArgs());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ToArgs_TopPOutOfRange_Throws(double topP)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { TopP = topP }.ToArgs());
    }

    [Fact]
    public void ToArgs_TopKNegative_Throws()
    {
      // Only a negative top-k is invalid; 0 (disabled) and values above the old 100 ceiling
      // are legitimate llama.cpp inputs (DMR-3).
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { TopK = -1 }.ToArgs());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(500)]
    public void ToArgs_TopK_ZeroAndAboveOldCeiling_Allowed(int topK)
    {
      // 0 disables top-k in llama.cpp and the upper bound is no longer artificially capped at 100.
      var args = new LlamaCppRuntimeFlags { TopK = topK }.ToArgs();
      Assert.Equal(new[] { "--top-k", topK.ToString(System.Globalization.CultureInfo.InvariantCulture) }, args);
    }

    [Fact]
    public void ToArgs_RepeatPenaltyOutOfRange_Throws()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { RepeatPenalty = 0.5 }.ToArgs());
    }

    [Fact]
    public void ToArgs_InvalidSplitMode_Throws()
    {
      var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { SplitMode = "bogus" }.ToArgs());
      // ParamName must name the user-facing property (consistent with the other flags),
      // not the private helper's "value" parameter.
      Assert.Equal(nameof(LlamaCppRuntimeFlags.SplitMode), ex.ParamName);
    }

    [Fact]
    public void ToArgs_ValidSplitModes_Accepted()
    {
      foreach (var mode in new[] { "none", "layer", "row" })
        Assert.Contains(mode, new LlamaCppRuntimeFlags { SplitMode = mode }.ToArgs());
    }

    [Fact]
    public void ToArgs_Empty_ReturnsEmpty()
    {
      Assert.Empty(new LlamaCppRuntimeFlags().ToArgs());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToArgs_NonPositivePerfCounts_Throw(int value)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { Threads = value }.ToArgs());
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { ThreadsBatch = value }.ToArgs());
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { BatchSize = value }.ToArgs());
    }

    [Fact]
    public void ToArgs_NegativeGpuIndices_Throw()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { GpuLayers = -1 }.ToArgs());
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { MainGpu = -1 }.ToArgs());
    }

    [Fact]
    public void ToArgs_ZeroGpuAndReasoning_Allowed()
    {
      // 0 is a valid GPU index / "no GPU layers" / "no reasoning budget".
      var args = new LlamaCppRuntimeFlags { GpuLayers = 0, MainGpu = 0, ReasoningBudget = 0 }.ToArgs();
      Assert.Contains("--n-gpu-layers", args);
      Assert.Contains("--main-gpu", args);
      Assert.Contains("--reasoning-budget", args);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2048)]
    public void ToArgs_ReasoningBudget_AllowsMinusOneAndNonNegative(int budget)
    {
      // -1 is llama.cpp's default (unrestricted reasoning) and 0 disables it; both must render
      // rather than being rejected as out of range (DMR-3).
      var args = new LlamaCppRuntimeFlags { ReasoningBudget = budget }.ToArgs();
      Assert.Equal(new[] { "--reasoning-budget", budget.ToString(System.Globalization.CultureInfo.InvariantCulture) }, args);
    }

    [Fact]
    public void ToArgs_ReasoningBudget_BelowMinusOne_Throws()
    {
      // -1 is the floor (unrestricted); anything more negative is genuinely invalid.
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { ReasoningBudget = -2 }.ToArgs());
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ToArgs_NonFiniteTemperature_Throws(double temp)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { Temperature = temp }.ToArgs());
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ToArgs_NonFiniteTopP_Throws(double value)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { TopP = value }.ToArgs());
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ToArgs_NonFiniteRopeFreqBase_Throws(double value)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppRuntimeFlags { RopeFreqBase = value }.ToArgs());
    }
  }
}
