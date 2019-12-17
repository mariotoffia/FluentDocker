using System.Text;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class VolumeMount
  {
    /// <summary>
    ///   Host path in MSYS or linux compatible format.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    ///   Inside docker container path in MSYS or linux compatible format.
    /// </summary>
    public string Destination { get; set; }

    /// <summary>
    ///   Mode of the mount (e.g. 'Z').
    /// </summary>
    public string Mode { get; set; }

    /// <summary>
    ///   Which access 'ro' or 'rw'.
    /// </summary>
    public bool RW { get; set; }

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
        sb.Append(':');
      }
      sb.Append(RW ? "rw " : "ro");

      return sb.ToString();
    }
  }
}
