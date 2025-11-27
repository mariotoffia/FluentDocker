namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   The short syntax variant only specifies the secret name.
  /// </summary>
  /// <remarks>
  ///   This grants the container access to the secret and mounts it at /run/secrets/[secret_name] within the container. The
  ///   source name and destination mountpoint are both set to the secret name.
  ///   Note: The secret must already exist or be defined in the top-level secrets configuration of this stack file, or stack
  ///   deployment fails. This requires docker compose file 3.1 or greater.
  /// </remarks>
  public class ShortSecret : ISecret
  {
    /// <summary>
    ///   Name of the secret.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///   The path, relative or absolute, to the secret file.
    /// </summary>
    /// <remarks>
    /// </remarks>
    public string FilePath { get; set; }

    /// <summary>
    ///   Defines the secret as external resource.
    /// </summary>
    /// <remarks>
    ///   If this is set to true, the <see cref="FilePath" /> is discarded since it is assumed that it is already
    ///   defined in Docker, either by running the docker secret create command or by another stack deployment. If the external
    ///   secret does not exist, the stack deployment fails with a secret not found error.
    /// </remarks>
    public bool IsExternal { get; set; }
  }
}
