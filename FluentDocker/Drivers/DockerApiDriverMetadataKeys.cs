namespace FluentDocker.Drivers
{
  /// <summary>
  /// Metadata keys understood by the Docker API driver.
  /// </summary>
  public static class DockerApiDriverMetadataKeys
  {
    /// <summary>
    /// Set to <c>true</c> to allow TLS certificate hostname/SAN mismatch while keeping chain validation enabled.
    /// </summary>
    public const string AllowTlsHostnameMismatch = "DockerApi.AllowTlsHostnameMismatch";

    /// <summary>
    /// Read-idle timeout for streamed response bodies, stored as <see cref="System.TimeSpan.Ticks"/>
    /// formatted with the invariant culture. Non-positive or unparsable values are ignored.
    /// </summary>
    public const string StreamIdleTimeoutTicks = "DockerApi.StreamIdleTimeoutTicks";
  }
}
