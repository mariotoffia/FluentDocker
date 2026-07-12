#nullable enable
namespace FluentDocker.Model.Events
{
  /// <summary>
  /// The action reported by a Docker event, e.g. the <c>Action</c> field of <c>docker events</c> output.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use stream driver ContainerEvent instead.")]
  public enum EventAction
  {
    /// <summary>The raw action string did not map to any known value.</summary>
    Unspecified,

    /// <summary>An image was pulled from a registry.</summary>
    Pull,

    /// <summary>A container was created (not yet started).</summary>
    Create,

    /// <summary>A container was started.</summary>
    Start,

    /// <summary>A container was sent a kill signal.</summary>
    Kill,

    /// <summary>A container's process exited.</summary>
    Die,

    /// <summary>A container was connected to a network.</summary>
    Connect,

    /// <summary>A container was disconnected from a network.</summary>
    Disconnect,

    /// <summary>A container was fully stopped.</summary>
    Stop,

    /// <summary>A container was removed from local disk.</summary>
    Destroy
  }
}
