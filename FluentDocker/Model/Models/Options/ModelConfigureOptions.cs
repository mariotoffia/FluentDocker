#nullable enable
using System.Collections.Generic;

namespace FluentDocker.Model.Models.Options
{
  /// <summary>
  /// Persistent per-model runtime configuration (maps to <c>docker model configure</c>).
  /// </summary>
  public sealed class ModelConfigureOptions
  {
    /// <summary>
    /// <c>--context-size N</c>. Use <c>null</c> to leave unchanged, or
    /// <see cref="ResetContextSize"/> to send <c>--context-size -1</c> (reset to
    /// the engine default).
    /// </summary>
    public int? ContextSize { get; init; }

    /// <summary>Emit <c>--context-size -1</c> to reset the context size to the engine default.</summary>
    public bool ResetContextSize { get; init; }

    /// <summary>
    /// Inference backend (engine) override. The default — <c>null</c> or <c>"auto"</c> —
    /// lets the runner pick the engine from the model format (gguf → llama.cpp,
    /// safetensors → vLLM), emitting no flag. An explicit value (e.g. <c>"vllm"</c>) is
    /// applied as <c>--backend</c> ONLY when the installed <c>docker model configure</c>
    /// supports it; otherwise <c>ConfigureAsync</c> fails with a clear message, since
    /// current DMR auto-selects and exposes no such flag.
    /// </summary>
    public string? Backend { get; init; }

    /// <summary>True when <see cref="Backend"/> is unset / <c>"auto"</c> (no <c>--backend</c> emitted).</summary>
    public bool IsAutoBackend =>
        string.IsNullOrWhiteSpace(Backend) || string.Equals(Backend, "auto", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Raw flags passed verbatim AFTER the <c>--</c> separator, e.g.
    /// <c>["--temp","0.7","--top-p","0.9"]</c>. A passthrough to the inference
    /// engine; see <see cref="LlamaCppRuntimeFlags"/> for a typed builder.
    /// </summary>
    public IReadOnlyList<string>? RuntimeFlags { get; init; }

    /// <summary>vLLM only — JSON passed to <c>--hf_overrides</c>.</summary>
    public string? HfOverridesJson { get; init; }
  }
}
