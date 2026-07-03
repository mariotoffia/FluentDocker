namespace FluentDocker.Model
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use system driver info responses instead.")]
  public sealed class DockerInfoBase
  {
    public string ClientVersion { get; set; }
    public string ClientApiVersion { get; set; }
    public string ServerVersion { get; set; }
    public string ServerApiVersion { get; set; }
    public string ServerOs { get; set; }

    public override string ToString()
    {
      return
        $"Client.Version = {ClientVersion} Client.ApiVersion = {ClientApiVersion} Server.Version = {ServerVersion} Server.ApiVersion = {ServerApiVersion} Server.Os = {ServerOs}";
    }
  }
}
