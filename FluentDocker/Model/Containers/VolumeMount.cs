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
    /// Renders the mount in <c>source:destination:mode,rw|ro</c> form, omitting empty segments.
    /// </summary>
    /// <returns>The Docker CLI-compatible volume specification string.</returns>
    public override string ToString()
    {
      var sb = new StringBuilder();
      if (!string.IsNullOrEmpty(Source))
      {
        sb.Append(Source);
      }

      if (!string.IsNullOrEmpty(Destination))
      {
        if (sb.Length > 0)
        {
          sb.Append(':');
        }
        sb.Append(Destination);
      }

      if (!string.IsNullOrEmpty(Mode))
      {
        if (sb.Length > 0)
        {
          sb.Append(':');
        }
        sb.Append(Mode);
      }

      if (sb.Length > 0)
      {
        sb.Append(string.IsNullOrEmpty(Mode) ? ':' : ',');
      }
      sb.Append(Rw ? "rw" : "ro");

      return sb.ToString();
    }
  }
}
