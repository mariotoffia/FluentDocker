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

    /// <summary>Select the backend (<c>--backend llama.cpp|vllm|diffusers</c>). Default ("leave unset") emits nothing.</summary>
    public ModelBackend Backend { get; init; }

    /// <summary>
    /// Raw flags passed verbatim AFTER the <c>--</c> separator, e.g.
    /// <c>["--temp","0.7","--top-p","0.9"]</c>. A passthrough to the inference
    /// engine; see <see cref="LlamaCppRuntimeFlags"/> for a typed builder.
    /// </summary>
    public IReadOnlyList<string> RuntimeFlags { get; init; }

    /// <summary>vLLM only — JSON passed to <c>--hf_overrides</c>.</summary>
    public string HfOverridesJson { get; init; }
  }
}
