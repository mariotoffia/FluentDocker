using System;
using System.Collections.Generic;
using System.Globalization;

namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// A typed, validated convenience over the raw llama.cpp runtime-flag
  /// passthrough. DMR passes these flags verbatim to the engine after a <c>--</c>
  /// separator; this type renders into that flat token list while validating the
  /// well-known ranges so a fat-fingered value fails fast with a clear message
  /// rather than a cryptic engine error. Unknown / <see cref="Raw"/> flags are
  /// not validated (passthrough freedom is intentional).
  /// </summary>
  public sealed class LlamaCppRuntimeFlags
  {
    // Sampling
    /// <summary><c>--temp</c> (default 0.8); range 0.0–2.0.</summary>
    public double? Temperature { get; init; }

    /// <summary><c>--top-k</c> (default 40); range 1–100.</summary>
    public int? TopK { get; init; }

    /// <summary><c>--top-p</c> (default 0.9); range 0.0–1.0.</summary>
    public double? TopP { get; init; }

    /// <summary><c>--min-p</c> (default 0.05); range 0.0–1.0.</summary>
    public double? MinP { get; init; }

    /// <summary><c>--repeat-penalty</c> (default 1.1); range 1.0–2.0.</summary>
    public double? RepeatPenalty { get; init; }

    // Performance
    /// <summary><c>--threads</c>.</summary>
    public int? Threads { get; init; }

    /// <summary><c>--threads-batch</c>.</summary>
    public int? ThreadsBatch { get; init; }

    /// <summary><c>--batch-size</c> (default 512).</summary>
    public int? BatchSize { get; init; }

    /// <summary><c>--mlock</c>.</summary>
    public bool? Mlock { get; init; }

    /// <summary><c>--no-mmap</c>.</summary>
    public bool? NoMmap { get; init; }

    // GPU
    /// <summary><c>--n-gpu-layers</c>.</summary>
    public int? GpuLayers { get; init; }

    /// <summary><c>--main-gpu</c> (default 0).</summary>
    public int? MainGpu { get; init; }

    /// <summary><c>--split-mode</c>; one of <c>none|layer|row</c>.</summary>
    public string SplitMode { get; init; }

    // Advanced
    /// <summary><c>--rope-freq-base</c>.</summary>
    public double? RopeFreqBase { get; init; }

    /// <summary><c>--rope-freq-scale</c>.</summary>
    public double? RopeFreqScale { get; init; }

    /// <summary><c>--rope-scaling</c>.</summary>
    public string RopeScaling { get; init; }

    /// <summary><c>--no-prefill-assistant</c>.</summary>
    public bool? NoPrefillAssistant { get; init; }

    /// <summary><c>--reasoning-budget</c> (default 0).</summary>
    public int? ReasoningBudget { get; init; }

    /// <summary>Escape hatch: extra raw flags appended verbatim (not validated).</summary>
    public IReadOnlyList<string> Raw { get; init; }

    /// <summary>
    /// Renders to the flat token list DMR expects after the <c>--</c> separator,
    /// validating the well-known ranges first.
    /// </summary>
    /// <returns>The rendered flag tokens.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A typed value is out of range.</exception>
    public IReadOnlyList<string> ToArgs()
    {
      var args = new List<string>();

      AddDouble(args, "--temp", Temperature, 0.0, 2.0, nameof(Temperature));
      AddInt(args, "--top-k", TopK, 1, 100, nameof(TopK));
      AddDouble(args, "--top-p", TopP, 0.0, 1.0, nameof(TopP));
      AddDouble(args, "--min-p", MinP, 0.0, 1.0, nameof(MinP));
      AddDouble(args, "--repeat-penalty", RepeatPenalty, 1.0, 2.0, nameof(RepeatPenalty));

      AddInt(args, "--threads", Threads, 1, null, nameof(Threads));
      AddInt(args, "--threads-batch", ThreadsBatch, 1, null, nameof(ThreadsBatch));
      AddInt(args, "--batch-size", BatchSize, 1, null, nameof(BatchSize));
      AddBool(args, "--mlock", Mlock);
      AddBool(args, "--no-mmap", NoMmap);

      AddInt(args, "--n-gpu-layers", GpuLayers, 0, null, nameof(GpuLayers));
      AddInt(args, "--main-gpu", MainGpu, 0, null, nameof(MainGpu));
      AddSplitMode(args, SplitMode, nameof(SplitMode));

      AddDouble(args, "--rope-freq-base", RopeFreqBase, null, null, nameof(RopeFreqBase));
      AddDouble(args, "--rope-freq-scale", RopeFreqScale, null, null, nameof(RopeFreqScale));
      AddString(args, "--rope-scaling", RopeScaling);
      AddBool(args, "--no-prefill-assistant", NoPrefillAssistant);
      AddInt(args, "--reasoning-budget", ReasoningBudget, 0, null, nameof(ReasoningBudget));

      if (Raw != null)
        args.AddRange(Raw);

      return args;
    }

    private static void AddDouble(List<string> args, string flag, double? value, double? min, double? max, string name)
    {
      if (!value.HasValue)
        return;

      var v = value.Value;
      if (!double.IsFinite(v))
        throw new ArgumentOutOfRangeException(name, v, $"{flag} must be a finite number.");

      if ((min.HasValue && v < min.Value) || (max.HasValue && v > max.Value))
        throw new ArgumentOutOfRangeException(name, v, $"{flag} must be in [{min}, {max}].");

      args.Add(flag);
      args.Add(v.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddInt(List<string> args, string flag, int? value, int? min, int? max, string name)
    {
      if (!value.HasValue)
        return;

      var v = value.Value;
      if ((min.HasValue && v < min.Value) || (max.HasValue && v > max.Value))
        throw new ArgumentOutOfRangeException(name, v, $"{flag} must be in [{min}, {max}].");

      args.Add(flag);
      args.Add(v.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddBool(List<string> args, string flag, bool? value)
    {
      if (value == true)
        args.Add(flag);
    }

    private static void AddString(List<string> args, string flag, string value)
    {
      if (string.IsNullOrEmpty(value))
        return;

      args.Add(flag);
      args.Add(value);
    }

    private static void AddSplitMode(List<string> args, string value, string name)
    {
      if (string.IsNullOrEmpty(value))
        return;

      if (value != "none" && value != "layer" && value != "row")
        throw new ArgumentOutOfRangeException(name, value, "--split-mode must be one of none|layer|row.");

      args.Add("--split-mode");
      args.Add(value);
    }
  }
}
