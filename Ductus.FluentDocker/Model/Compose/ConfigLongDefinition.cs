namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// The long syntax provides more granularity in how the config is created within the service’s task containers.
  /// </summary>
  /// <remarks>
  /// Note: config definitions are only supported in version 3.3 and higher of the compose file format.
  /// </remarks>
  public sealed class ConfigLongDefinition
  {
    /// <summary>
    ///  The name of the config as it exists in Docker.
    /// </summary>
    public string Source { get; set; }
    /// <summary>
    /// The path and name of the file to be mounted in the service’s task containers. Defaults to "/source" if not
    /// specified.
    /// </summary>
    public string Target { get; set; }
    /// <summary>
    /// The numeric UID that owns the mounted config file within in the service’s task containers. Defaults
    /// to 0 on Linux if not specified. Not supported on Windows.
    /// </summary>
    public string Uid { get; set; }
    /// <summary>
    /// The numeric GID that owns the mounted config file within in the service’s task containers. Defaults
    /// to 0 on Linux if not specified. Not supported on Windows.
    /// </summary>
    public string Gid { get; set; }
    /// <summary>
    ///  The permissions for the file that is mounted within the service’s task containers, in octal notation. 
    /// </summary>
    /// <remarks>
    /// For instance, 0444 represents world-readable. The default is 0444. Configs cannot be writable because they
    /// are mounted in a temporary filesystem, so if you set the writable bit, it is ignored. The executable bit can
    /// be set. If you aren’t familiar with UNIX file permission modes, you may find this permissions calculator useful.
    /// </remarks>
    public string Mode { get; set; }
  }
}