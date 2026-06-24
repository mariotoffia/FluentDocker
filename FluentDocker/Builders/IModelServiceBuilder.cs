using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Fluent builder for a lifecycle-first <see cref="IModelService"/> (a managed,
  /// single-model handle that loads on start and can unload on dispose).
  /// </summary>
  public interface IModelServiceBuilder : IDriverScopedBuilder
  {
    /// <summary>Sets the persistent context size.</summary>
    IModelServiceBuilder WithContextSize(int tokens);

    /// <summary>
    /// Selects the inference backend/engine. The default (<c>"auto"</c> / unset) lets the
    /// runner pick the engine from the model format; an explicit value is applied only
    /// when the installed <c>docker model configure</c> supports <c>--backend</c>.
    /// </summary>
    IModelServiceBuilder WithBackend(string backend);

    /// <summary>Configures load (run) options.</summary>
    IModelServiceBuilder WithRunOptions(Action<ModelRunOptionsBuilder> configure);

    /// <summary>When true, the model is NOT unloaded on dispose.</summary>
    IModelServiceBuilder KeepRunning(bool keep = true);

    /// <summary>Pulls the model at build time if it is not present.</summary>
    IModelServiceBuilder PullIfMissing(bool pull = true);

    /// <summary>Builds the model service (synchronous; prefer <see cref="BuildAsync"/>).</summary>
    IModelService Build();

    /// <summary>
    /// Builds the model service asynchronously, honoring <paramref name="cancellationToken"/>
    /// for the build-time pull/configure work.
    /// </summary>
    Task<IModelService> BuildAsync(CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Small fluent builder for <see cref="FluentDocker.Model.Models.Options.ModelRunOptions"/>.
  /// </summary>
  public sealed class ModelRunOptionsBuilder
  {
    private bool _detach;
    private bool _debug;

    /// <summary>Run detached (load and keep resident).</summary>
    public ModelRunOptionsBuilder WithDetach(bool detach = true)
    {
      _detach = detach;
      return this;
    }

    /// <summary>Enable engine debug output.</summary>
    public ModelRunOptionsBuilder WithDebug(bool debug = true)
    {
      _debug = debug;
      return this;
    }

    internal Model.Models.Options.ModelRunOptions Build() => new()
    {
      Detach = _detach,
      Debug = _debug
    };
  }
}
