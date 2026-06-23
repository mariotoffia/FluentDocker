using System;
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

    /// <summary>Selects the backend.</summary>
    IModelServiceBuilder WithBackend(string backend);

    /// <summary>Configures load (run) options.</summary>
    IModelServiceBuilder WithRunOptions(Action<ModelRunOptionsBuilder> configure);

    /// <summary>When true, the model is NOT unloaded on dispose.</summary>
    IModelServiceBuilder KeepRunning(bool keep = true);

    /// <summary>Pulls the model at build time if it is not present.</summary>
    IModelServiceBuilder PullIfMissing(bool pull = true);

    /// <summary>Builds the model service.</summary>
    IModelService Build();
  }

  /// <summary>
  /// Small fluent builder for <see cref="FluentDocker.Model.Models.Options.ModelRunOptions"/>.
  /// </summary>
  public sealed class ModelRunOptionsBuilder
  {
    private bool _detach;
    private bool _ignoreRuntimeMemoryCheck;
    private bool _debug;
    private string _backend;

    /// <summary>Run detached (load and keep resident).</summary>
    public ModelRunOptionsBuilder WithDetach(bool detach = true)
    {
      _detach = detach;
      return this;
    }

    /// <summary>Skip the host runtime-memory check.</summary>
    public ModelRunOptionsBuilder IgnoreRuntimeMemoryCheck(bool ignore = true)
    {
      _ignoreRuntimeMemoryCheck = ignore;
      return this;
    }

    /// <summary>Override the backend for this run.</summary>
    public ModelRunOptionsBuilder WithBackend(string backend)
    {
      _backend = backend;
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
      IgnoreRuntimeMemoryCheck = _ignoreRuntimeMemoryCheck,
      Backend = _backend,
      Debug = _debug
    };
  }
}
