#nullable enable
namespace FluentDocker.Model.Builders
{
  /// <summary>Access mode for a container mount, rendered as Docker's <c>ro</c>/<c>rw</c> mount option.</summary>
  public enum MountType
  {
    /// <summary>Mount is read-only (Docker <c>ro</c>).</summary>
    ReadOnly = 0,
    /// <summary>Mount is read-write (Docker <c>rw</c>).</summary>
    ReadWrite = 1
  }
}
