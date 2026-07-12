#nullable enable
namespace FluentDocker.Model.Images
{
  /// <summary>
  /// Which images to remove, mirroring Docker Compose's <c>down --rmi &lt;type&gt;</c> option.
  /// </summary>
  public enum ImageRemovalOption
  {
    /// <summary>Do not remove any images (default).</summary>
    None = 0,

    /// <summary>Remove only images that don't have a custom tag (compose <c>--rmi local</c>).</summary>
    Local = 1,

    /// <summary>Remove all images used by any service (compose <c>--rmi all</c>).</summary>
    All = 2
  }
}
