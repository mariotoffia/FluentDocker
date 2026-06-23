using System;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// The runner's status (the result of <c>docker model status</c> / a <c>/_ping</c>).
  /// </summary>
  public sealed class ModelRunnerStatus
  {
    /// <summary>True when the runner is running and reachable.</summary>
    public bool Running { get; init; }

    /// <summary>The active backend, e.g. <c>llama.cpp</c>.</summary>
    public string Backend { get; init; }

    /// <summary>The resolved inference endpoint.</summary>
    public Uri Endpoint { get; init; }

    /// <summary>A remediation / error message, populated when <see cref="Running"/> is false.</summary>
    public string Error { get; init; }
  }
}
