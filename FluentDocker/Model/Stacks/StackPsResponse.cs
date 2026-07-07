#nullable enable
namespace FluentDocker.Model.Stacks
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use IStackDriver task response models instead.")]
  public class StackPsResponse
  {
    public string Id { get; set; } = null!;
    /// <summary>
    /// The stack that this <see cref="Name"/> belongs to.
    /// </summary>
    public string Stack { get; set; } = null!;
    /// <summary>
    /// The name of the instance without the stack name
    /// </summary>
    /// <remarks>
    /// The output of a docker stack will emit stack_name where this property is just the name.
    /// </remarks>
    public string Name { get; set; } = null!;
    /// <summary>
    /// The name of the image without any version.
    /// </summary>
    public string Image { get; set; } = null!;
    /// <summary>
    /// The version of the <see cref="Image"/>.
    /// </summary>
    public string ImageVersion { get; set; } = null!;
    /// <summary>
    /// The node name.
    /// </summary>
    public string Node { get; set; } = null!;
    /// <summary>
    /// The desired state.
    /// </summary>
    public string DesiredState { get; set; } = null!;
    /// <summary>
    /// The actual state. This may be different from <see cref="DesiredState"/>.
    /// </summary>
    /// <remarks>
    /// Even if this state is the <see cref="DesiredState"/> it cannot be compared
    /// directly since it may e.g. be 'Running since 2 minutes ago' whereas the <see cref="DesiredState"/>
    /// is 'Running'.
    /// </remarks>
    public string CurrentState { get; set; } = null!;
    /// <summary>
    /// If any error otherwise <see cref="string.Empty"/>.
    /// </summary>
    public string Error { get; set; } = null!;
    /// <summary>
    /// Any exposed ports, otherwise <see cref="string.Empty"/>.
    /// </summary>
    public string Ports { get; set; } = null!;
  }
}
