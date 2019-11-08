namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class HostBuilderConfig
  {
    public bool UseNative { get; set; } = false;
    public string Name { get; set; } = "default";
    public int MemoryMb { get; set; } = 1024;
    public int CpuCount { get; set; } = 1;
    public string Driver { get; set; } = "virtualbox";
    public int StorageSizeMb { get; set; } = 20*1024*1024;
  }
}
