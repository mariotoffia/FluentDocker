#nullable enable
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// A progress event emitted while pulling a model (streamed from
  /// <c>docker model pull</c> or <c>POST /models/create</c>).
  /// </summary>
  public sealed class ModelPullProgress
  {
    /// <summary>The status phase, e.g. <c>Downloading</c>, <c>Verifying</c>.</summary>
    public string? Status { get; init; }

    /// <summary>The layer / artifact being transferred, if known.</summary>
    public string? Layer { get; init; }

    /// <summary>The bytes transferred so far.</summary>
    public long Current { get; init; }

    /// <summary>The total bytes to transfer (0 when unknown).</summary>
    public long Total { get; init; }

    /// <summary>The fraction complete in <c>[0,1]</c> (0 when <see cref="Total"/> is 0).</summary>
    [JsonIgnore]
    public double Fraction => Total > 0 ? (double)Current / Total : 0d;
  }
}
