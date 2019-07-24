using System.Net;

namespace Ductus.FluentDocker.Model.Machines
{
  public sealed class MachineConfiguration
  {
    public string Name { get; set; }
    public string DriverName { get; set; }
    public string StorePath { get; set; }

    /// <summary>
    ///   Gets or sets the ip address. If none <see cref="IPAddress.None" /> is set.
    /// </summary>
    /// <remarks>
    ///   If this is not set, check the <see cref="Hostname" /> to see if it has a hostname instead.
    /// </remarks>
    public IPAddress IpAddress { get; set; }

    /// <summary>
    ///   The hostname of the machine.
    /// </summary>
    /// <remarks>
    ///   This may be set using eg. --generic-ip-address=my-host-name and therefore
    ///   docker-machine will not emit an <see cref="IpAddress" />, instead it will return
    ///   a hostname of the machine.
    /// </remarks>
    public string Hostname { get; set; }

    public int MemorySizeMb { get; set; }
    public int StorageSizeMb { get; set; }
    public int CpuCount { get; set; }
    public bool RequireTls { get; set; }
    public MachineAuthConfig AuthConfig { get; set; }
  }
}