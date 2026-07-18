#nullable enable
using System.Text;

namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// A single bind-mount/volume specification, e.g. the <c>source:destination:mode</c> form
  /// accepted by <c>docker run -v</c>.
  /// </summary>
  public sealed class VolumeMount
  {
    /// <summary>
    ///   Host path in MSYS or linux compatible format.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    ///   Inside docker container path in MSYS or linux compatible format.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    ///   Mode of the mount (e.g. 'Z').
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    ///   Which access 'ro' or 'rw'.
    /// </summary>
    public bool Rw { get; set; }

    /// <summary>
    /// Renders the mount in <c>source:destination[:mode],rw|ro</c> form, omitting empty segments.
    /// </summary>
    /// <returns>
    /// The Docker CLI-compatible volume specification string, or <see cref="string.Empty"/> when
    /// <see cref="Destination"/> is unset — such a partial DTO cannot produce a valid mount spec.
    /// </returns>
    /// <remarks>
    /// <see cref="Rw"/> is a plain <see cref="bool"/>, so an absent value is indistinguishable from
    /// an explicit read-only mount; the access suffix (<c>rw</c>/<c>ro</c>) is rendered whenever a
    /// destination exists, defaulting to <c>ro</c> when <see cref="Rw"/> is <c>false</c>.
    /// </remarks>
    public override string ToString()
    {
      if (string.IsNullOrEmpty(Destination))
        return string.Empty;

      var sb = new StringBuilder();
      if (!string.IsNullOrEmpty(Source))
      {
        sb.Append(Source).Append(':');
      }

      sb.Append(Destination);

      if (!string.IsNullOrEmpty(Mode))
      {
        sb.Append(':').Append(Mode);
      }

      sb.Append(string.IsNullOrEmpty(Mode) ? ':' : ',').Append(Rw ? "rw" : "ro");

      return sb.ToString();
    }
  }
}
