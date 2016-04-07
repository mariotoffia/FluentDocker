using System.Net;

namespace Ductus.FluentDocker.Model
{
  public sealed class MachineConfiguration
  {
    public string Name { get; set; }
    public string DriverName { get; set; }
    public string StorePath { get; set; }

    /// <summary>
    ///   Gets or sets the ip address. If none <see cref="IPAddress.None" /> is set.
    /// </summary>
    public IPAddress IpAddress { get; set; }

    public int MemorySizeMb { get; set; }
    public int StorageSizeMb { get; set; }
    public int CpuCount { get; set; }
    public bool RequireTls { get; set; }
    public MachineAuthConfig AuthConfig { get; set; }
  }
}