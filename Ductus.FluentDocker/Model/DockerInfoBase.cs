namespace Ductus.FluentDocker.Model
{
  public sealed class DockerInfoBase
  {
    public string ClientVersion { get; set; }
    public string ClientApiVersion { get; set; }
    public string ServerVersion { get; set; }
    public string ServerApiVersion { get; set; }

    public override string ToString()
    {
      return
        $"Client.Version = {ClientVersion} Client.ApiVersion = {ClientApiVersion} Server.Version = {ServerVersion} Server.ApiVersion = {ServerApiVersion}";
    }
  }
}