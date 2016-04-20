namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class Container
  {
    public string Image { get; set; }
    public string ResolvConfPath { get; set; }
    public string HostnamePath { get; set; }
    public string HostsPath { get; set; }
    public string LogPath { get; set; }
    public string Name { get; set; }
    public int RestartCount { get; set; }
    public string Driver { get; set; }
    public string [] Args { get; set; }
    public ContainerState State { get; set; }
    public ContainerMount[] Mounts { get; set; } 
    public ContainerConfig Config { get; set; }
    public ContainerNetworkSettings NetworkSettings { get; set; }
  }
}
