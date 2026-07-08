using System;

namespace FluentDocker.Services
{
  /// <summary>
  /// An inference-only model runner (OpenAI-compatible endpoint). Exposes only the
  /// inference plane and async disposal — no store/engine members that would throw
  /// <see cref="NotSupportedException"/>.
  /// </summary>
  /// <remarks>
  /// Use this interface when you only need to perform chat/completion/embeddings against
  /// an OpenAI-compatible endpoint (e.g. a Docker Model Runner workload, a container-
  /// injected endpoint, or any third-party endpoint). <see cref="IModelRunner"/> is the
  /// broader interface that additionally covers model management and runtime control.
  /// No built-in retry/backoff is performed; callers own retry policy.
  /// </remarks>
  public interface IInferenceModelRunner : IModelInference, IAsyncDisposable { }
}
