#nullable enable
using System;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// A model currently loaded / running in the runner (the result of
  /// <c>docker model ps</c>).
  /// </summary>
  public sealed class RunningModel
  {
    /// <summary>The model reference.</summary>
    public ModelReference? Reference { get; init; }

    /// <summary>The inference backend, e.g. <c>llama.cpp</c>.</summary>
    public string? Backend { get; init; }

    /// <summary>The mode, e.g. <c>loaded</c>, <c>running</c>, <c>completion</c>, <c>embedding</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>Resident memory in bytes (0 when unknown).</summary>
    public long MemoryBytes { get; init; }

    /// <summary>The last-used timestamp (null when unknown).</summary>
    public DateTime? LastUsed { get; init; }
  }
}
