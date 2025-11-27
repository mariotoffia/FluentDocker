namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// Definition of a temporary filesystem.
  /// </summary>
  /// <remarks>
  /// Note: Version 3.6 file format and up when using other than <see cref="Target"/>.
  /// </remarks>
  public sealed class TmpFsDefinition
  {
    /// <summary>
    /// Specifies the type. Default is tmpfs.
    /// </summary>
    public string Type { get; set; } = "tmpfs";
    /// <summary>
    /// The target mount point e.g. /app.
    /// </summary>
    public string Target { get; set; }
    /// <summary>
    /// specifies the size of the tmpfs mount in bytes. Unlimited by default.
    /// </summary>
    public long Size { get; set; } = -1;
  }
}
