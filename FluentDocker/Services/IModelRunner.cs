using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;

namespace FluentDocker.Services
{
  /// <summary>
  /// Convenience façade composing store + engine + inference, plus ergonomic
  /// helpers bound to a default model. This is what <c>UseModelRunner()</c>
  /// returns. Implementations MAY support only a subset; callers can feature-detect
  /// via <see cref="Capabilities"/>.
  /// No built-in retry/backoff is performed; callers own retry policy for retryable
  /// inference failures (for example 429/503 and transient transport errors).
  /// </summary>
  public interface IModelRunner : IModelStore, IModelEngine, IModelInference, IAsyncDisposable
  {
    /// <summary>The default model bound at build time, or null when none was configured.</summary>
    ModelReference? DefaultModel { get; }

    /// <summary>
    /// The context/default endpoint used for diagnostics. When inference is routed through
    /// <c>WithInferenceDriver(...)</c>, this does not describe the injected driver's target.
    /// </summary>
    Uri Endpoint { get; }

    /// <summary>
    /// Static feature-detection for the backing implementation. This reports which
    /// ports/routes the adapter can serve, not whether the endpoint is currently reachable.
    /// </summary>
    ModelRunnerCapabilities Capabilities { get; }

    /// <summary>One-shot chat against the default model.</summary>
    Task<string> ChatAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Streaming token-by-token chat against the default model.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Embed a single string with the default (or specified) model.</summary>
    Task<IReadOnlyList<float>> EmbedAsync(string text, ModelReference? model = null, CancellationToken cancellationToken = default);
  }
}
