#nullable enable
namespace FluentDocker.Model
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Use system driver info responses instead.")]
  public sealed class DockerInfoBase
  {
    public string ClientVersion { get; set; } = null!;
    public string ClientApiVersion { get; set; } = null!;
    public string ServerVersion { get; set; } = null!;
    public string ServerApiVersion { get; set; } = null!;
    public string ServerOs { get; set; } = null!;

    public override string ToString()
    {
      return
        $"Client.Version = {ClientVersion} Client.ApiVersion = {ClientApiVersion} Server.Version = {ServerVersion} Server.ApiVersion = {ServerApiVersion} Server.Os = {ServerOs}";
    }
  }
}
