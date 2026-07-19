using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
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

    /// <summary>Sets raw engine runtime flags (applied via configure at build).</summary>
    IModelServiceBuilder WithRuntimeFlags(params string[] flags);

    /// <summary>
    /// Overrides the inference endpoint, optionally with transport configuration and a
    /// bearer API key (parity with <see cref="IModelRunnerBuilder.WithEndpoint"/>).
    /// </summary>
    IModelServiceBuilder WithEndpoint(ModelRunnerEndpoint endpoint,
        ModelApiConnectionConfig? config = null, string? apiKey = null);

    /// <summary>Routes inference to an explicit <see cref="IModelInferenceDriver"/> (caller-owned).</summary>
    IModelServiceBuilder WithInferenceDriver(IModelInferenceDriver inference);

    /// <summary>Routes inference to the driver registered under another <paramref name="driverId"/>.</summary>
    IModelServiceBuilder WithInferenceDriver(string driverId);

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
    private bool _detach = true;
    private bool _debug;
    private string? _openAiUrl;
    private bool _webSearch;
    private int? _contextSize;
    private System.Collections.Generic.IReadOnlyList<string>? _runtimeFlags;

    /// <summary>Load detached so the call returns once resident (default true).</summary>
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

    /// <summary>Override the OpenAI-compatible base URL the run session targets.</summary>
    public ModelRunOptionsBuilder WithOpenAiUrl(string url)
    {
      _openAiUrl = url;
      return this;
    }

    /// <summary>Enable the web-search tool for the session.</summary>
    public ModelRunOptionsBuilder WithWebSearch(bool enable = true)
    {
      _webSearch = enable;
      return this;
    }

    /// <summary>Persistent context window (applied via <c>configure</c> before the run).</summary>
    public ModelRunOptionsBuilder WithContextSize(int tokens)
    {
      if (tokens <= 0)
        throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "Context size must be greater than zero.");

      _contextSize = tokens;
      return this;
    }

    /// <summary>Raw inference-engine flags (applied via <c>configure</c> before the run).</summary>
    public ModelRunOptionsBuilder WithRuntimeFlags(params string[] flags)
    {
      _runtimeFlags = flags;
      return this;
    }

    internal Model.Models.Options.ModelRunOptions Build() => new()
    {
      Detach = _detach,
      Debug = _debug,
      OpenAiUrl = _openAiUrl,
      WebSearch = _webSearch,
      ContextSize = _contextSize,
      RuntimeFlags = _runtimeFlags
    };
  }
}
