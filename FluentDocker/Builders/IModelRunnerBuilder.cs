using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Fluent builder for an inference-first <see cref="IModelRunner"/>.
  /// </summary>
  public interface IModelRunnerBuilder : IDriverScopedBuilder
  {
    /// <summary>Binds a default model by string reference.</summary>
    IModelRunnerBuilder ForModel(string reference);

    /// <summary>Binds a default model.</summary>
    IModelRunnerBuilder ForModel(ModelReference reference);

    /// <summary>Sets the persistent context size (applied via configure at build).</summary>
    IModelRunnerBuilder WithContextSize(int tokens);

    /// <summary>
    /// Selects the inference backend/engine, applied via configure at build. The default
    /// (<c>"auto"</c> / unset) lets the runner pick the engine from the model format and
    /// emits nothing. An explicit value (e.g. <c>"vllm"</c>) is applied only when the
    /// installed <c>docker model configure</c> supports <c>--backend</c>; otherwise the
    /// build fails with a clear message (current DMR auto-selects).
    /// </summary>
    IModelRunnerBuilder WithBackend(string backend);

    /// <summary>Sets raw engine runtime flags (applied via configure at build).</summary>
    IModelRunnerBuilder WithRuntimeFlags(params string[] flags);

    /// <summary>Overrides the inference endpoint.</summary>
    IModelRunnerBuilder WithEndpoint(ModelRunnerEndpoint endpoint);

    /// <summary>
    /// Routes inference to an explicit <see cref="IModelInferenceDriver"/> instead of
    /// the scoped driver's inference port — e.g. to run inference on a different
    /// engine/endpoint while management/runtime stay on the scoped driver. The
    /// supplied driver's lifetime is owned by the caller (not disposed by the runner).
    /// Takes precedence over <see cref="WithEndpoint"/>.
    /// </summary>
    IModelRunnerBuilder WithInferenceDriver(IModelInferenceDriver inference);

    /// <summary>
    /// Routes inference to the <see cref="IModelInferenceDriver"/> registered under a
    /// different <paramref name="driverId"/> in the same kernel (resolved at build
    /// time), while management/runtime stay on the scoped driver.
    /// </summary>
    IModelRunnerBuilder WithInferenceDriver(string driverId);

    /// <summary>Pulls the default model at build time if it is not present.</summary>
    IModelRunnerBuilder PullIfMissing(bool pull = true);

    /// <summary>Builds the runner.</summary>
    IModelRunner Build();

    /// <summary>Builds the runner asynchronously.</summary>
    Task<IModelRunner> BuildAsync(CancellationToken cancellationToken = default);
  }
}
