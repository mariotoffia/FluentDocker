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
  }
}
