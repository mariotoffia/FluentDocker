#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FluentDocker.Common;

namespace FluentDocker.Model.Volumes
{
  /// <summary>
  /// Represents a Docker/Podman volume as returned by volume inspect or list.
  /// </summary>
  public sealed class Volume
  {
    /// <summary>Timestamp when the volume was created.</summary>
    [JsonPropertyName("CreatedAt")]
    public DateTimeOffset Created { get; set; }

    /// <summary>Volume driver name (e.g., "local").</summary>
    public string Driver { get; set; } = null!;

    /// <summary>Unique name of the volume.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Scope of the volume ("local" or "global"). Absent from some engine responses.</summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Filesystem path where the volume data is stored on the host. Absent from some engine responses.
    /// </summary>
    public string? Mountpoint { get; set; }

    /// <summary>
    /// User-defined labels attached to the volume. Legacy compact string input cannot represent comma-containing values.
    /// </summary>
    [JsonConverter(typeof(LenientStringDictionaryConverter))]
    public Dictionary<string, string>? Labels { get; set; }

    /// <summary>
    /// Driver-specific options used when creating the volume. Legacy compact string input cannot represent comma-containing values.
    /// </summary>
    [JsonConverter(typeof(LenientStringDictionaryConverter))]
    public Dictionary<string, string>? Options { get; set; }
  }
}
