namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  ///   The long syntax provides more granularity in how the secret is created within the service’s task containers.
  /// </summary>
  /// <remarks>
  ///   Note: The secret must already exist or be defined in the top-level secrets configuration of this stack file, or stack
  ///   deployment fails. This requires docker compose file 3.1 or greater.
  /// </remarks>
  /// <example>
  ///   The following example sets name of the my_secret to redis_secret within the container, sets the mode to 0440
  ///   (group-readable) and sets the user and group to 103. The redis service does not have access to the my_other_secret
  ///   secret.
  ///   version: "3.1"
  ///   services:
  ///     redis:
  ///     image: redis:latest
  ///     deploy:
  ///       replicas: 1
  ///     secrets:
  ///       - source: my_secret
  ///         target: redis_secret
  ///         uid: '103'
  ///         gid: '103'
  ///         mode: 0440
  ///   secrets:
  ///     my_secret:
  ///       file: ./my_secret.txt
  ///     my_other_secret:
  ///       external: true
  /// </example>
  public sealed class LongSecret : ISecret
  {
    /// <summary>
    ///   The name of the secret as it exists in Docker.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    ///   The name of the file to be mounted in /run/secrets/ in the service’s task containers. Defaults to source if not
    ///   specified.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    ///   The numeric UID that owns the file within /run/secrets/ in the service’s task containers. It default to 0 if not
    ///   specified.
    /// </summary>
    public int Uid { get; set; }

    /// <summary>
    ///   The numeric GID that owns the file within /run/secrets/ in the service’s task containers. It default to 0 if not
    ///   specified.
    /// </summary>
    public int Gid { get; set; }

    /// <summary>
    ///   The permissions for the file to be mounted in /run/secrets/ in the service’s task containers, in octal notation.
    /// </summary>
    /// <remarks>
    ///   For instance, 0444 represents world-readable.
    ///   Secrets cannot be writable because they are mounted in a temporary filesystem, so if you set the writable bit, it is
    ///   ignored. The executable bit can be set. If you aren’t familiar with UNIX file permission modes, you may find this
    ///   permissions calculator useful. Defaults to 0444.
    /// </remarks>
    public string Mode { get; set; } = "0444";
  }
}
