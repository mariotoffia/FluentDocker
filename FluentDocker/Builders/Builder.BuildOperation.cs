#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Represents a build operation to be executed.
  /// </summary>
  internal sealed class BuildOperation
  {
    public FluentDockerKernel Kernel { get; set; }
    public string DriverId { get; set; }
    public object ResourceBuilder { get; set; }
    public Func<TimeSpan, CancellationToken, Task<IServiceAsync>> ExecuteAsync { get; set; }
    public Func<IServiceAsync> GetFailedService { get; set; }
    public Action ResetForRetry { get; set; }
    public Func<IServiceAsync, bool> ForceRemoveOnFailure { get; set; }
    public Func<IServiceAsync, string?> FailureKeepReason { get; set; }
    public string ResourceKind { get; set; }
    public string ResourceName { get; set; }
    public IReadOnlyCollection<string> NetworkReferences { get; set; } = [];
    public IReadOnlyCollection<string> VolumeReferences { get; set; } = [];
    public IReadOnlyCollection<string> LinkReferences { get; set; } = [];
    public IReadOnlyCollection<string> ImageReferences { get; set; } = [];
    public IReadOnlyCollection<string> PodReferences { get; set; } = [];

    /// <summary>
    /// Optional post-start callback for executing deferred operations
    /// (e.g., wait conditions on linked containers).
    /// </summary>
    public Func<CancellationToken, Task> PostStartAsync { get; set; }

    public bool AllowCleanExit { get; set; } = true;
    public Func<bool> StartDeferred { get; set; } = () => false;
    public long StartupTimeoutMs { get; set; } = 3000;
    public int StartupPollIntervalMs { get; set; } = 100;
  }
}
