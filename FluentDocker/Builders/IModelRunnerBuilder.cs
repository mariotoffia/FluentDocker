using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Selects the backend (applied via configure at build).</summary>
    IModelRunnerBuilder WithBackend(string backend);

    /// <summary>Sets raw engine runtime flags (applied via configure at build).</summary>
    IModelRunnerBuilder WithRuntimeFlags(params string[] flags);

    /// <summary>Overrides the inference endpoint.</summary>
    IModelRunnerBuilder WithEndpoint(ModelRunnerEndpoint endpoint);

    /// <summary>Pulls the default model at build time if it is not present.</summary>
    IModelRunnerBuilder PullIfMissing(bool pull = true);

    /// <summary>Builds the runner.</summary>
    IModelRunner Build();

    /// <summary>Builds the runner asynchronously.</summary>
    Task<IModelRunner> BuildAsync(CancellationToken cancellationToken = default);
  }
}
