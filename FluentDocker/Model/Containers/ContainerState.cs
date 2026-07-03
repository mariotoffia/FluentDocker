using System;

// ReSharper disable InconsistentNaming

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// Runtime state section of container inspect output.
  /// </summary>
  public sealed class ContainerState
  {
    /// <summary>Raw runtime status string, such as running, exited, or created.</summary>
    public string Status { get; set; }

    /// <summary>Whether the container is currently running.</summary>
    public bool Running { get; set; }

    /// <summary>Whether the container is paused.</summary>
    public bool Paused { get; set; }

    /// <summary>Whether the runtime is restarting the container.</summary>
    public bool Restarting { get; set; }

    /// <summary>Whether the container was killed by the out-of-memory killer.</summary>
    public bool OOMKilled { get; set; }

    /// <summary>Whether the runtime marks the container as dead.</summary>
    public bool Dead { get; set; }

    /// <summary>Process ID of the container init process.</summary>
    public int Pid { get; set; }

    /// <summary>Last process exit code.</summary>
    public long ExitCode { get; set; }

    /// <summary>Runtime error message, when present.</summary>
    public string Error { get; set; }

    /// <summary>Timestamp when the container started.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Timestamp when the container finished.</summary>
    public DateTime FinishedAt { get; set; }

    /// <summary>Container health status and recent health checks.</summary>
    public Health Health { get; set; }
  }
}
