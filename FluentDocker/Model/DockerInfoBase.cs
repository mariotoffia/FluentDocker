#nullable enable
namespace FluentDocker.Model
{
  /// <summary>
  /// Legacy holder for Docker client/server version and OS info, mirroring <c>docker version</c> output.
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use system driver info responses instead.")]
  public sealed class DockerInfoBase
  {
    /// <summary>The Docker CLI client version, e.g. <c>docker version --format {{.Client.Version}}</c>.</summary>
    public string ClientVersion { get; set; } = null!;
    /// <summary>The Docker Engine API version the client negotiated.</summary>
    public string ClientApiVersion { get; set; } = null!;
    /// <summary>The Docker daemon (server) version.</summary>
    public string ServerVersion { get; set; } = null!;
    /// <summary>The Docker Engine API version the daemon supports.</summary>
    public string ServerApiVersion { get; set; } = null!;
    /// <summary>The daemon host operating system, e.g. <c>linux</c>.</summary>
    public string ServerOs { get; set; } = null!;

    /// <summary>Renders all client/server version and OS fields on a single line.</summary>
    public override string ToString()
    {
      return
        $"Client.Version = {ClientVersion} Client.ApiVersion = {ClientApiVersion} Server.Version = {ServerVersion} Server.ApiVersion = {ServerApiVersion} Server.Os = {ServerOs}";
    }
  }
}
