using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentDocker.Extensions;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker-machine create command.
  /// </summary>
  public struct MachineCreateCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Driver to create machine with.</summary>
    public string Driver { get; set; }
    /// <summary>Driver-specific options.</summary>
    public IList<string> DriverOptions { get; set; }
    /// <summary>Memory size in MB.</summary>
    public int? MemoryMb { get; set; }
    /// <summary>Disk size in MB.</summary>
    public int? DiskSizeMb { get; set; }
    /// <summary>Number of CPUs.</summary>
    public int? CpuCount { get; set; }
    /// <summary>Custom URL to Docker engine tarball.</summary>
    public string EngineInstallUrl { get; set; }
    /// <summary>Engine labels.</summary>
    public IList<string> EngineLabels { get; set; }
    /// <summary>Engine storage driver.</summary>
    public string EngineStorageDriver { get; set; }
    /// <summary>Engine environment variables.</summary>
    public IList<string> EngineEnv { get; set; }
    /// <summary>Engine insecure registries.</summary>
    public IList<string> EngineInsecureRegistry { get; set; }
    /// <summary>Engine registry mirrors.</summary>
    public IList<string> EngineRegistryMirror { get; set; }
    /// <summary>Engine options.</summary>
    public IList<string> EngineOpts { get; set; }
    /// <summary>Configure swarm options.</summary>
    public bool Swarm { get; set; }
    /// <summary>TLS SAN.</summary>
    public IList<string> TlsSan { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("-d ", Driver);

      if (DriverOptions != null)
      {
        foreach (var opt in DriverOptions)
        {
          sb.Append($" {opt}");
        }
      }

      if (MemoryMb.HasValue && !string.IsNullOrEmpty(Driver))
        sb.Append($" --{Driver}-memory \"{MemoryMb.Value}\"");
      if (DiskSizeMb.HasValue && !string.IsNullOrEmpty(Driver))
        sb.Append($" --{Driver}-disk-size \"{DiskSizeMb.Value}\"");
      if (CpuCount.HasValue && !string.IsNullOrEmpty(Driver))
        sb.Append($" --{Driver}-cpu-count \"{CpuCount.Value}\"");

      sb.OptionIfExists("--engine-install-url ", EngineInstallUrl);
      sb.OptionIfExists("--engine-label ", EngineLabels?.ToArray());
      sb.OptionIfExists("--engine-storage-driver ", EngineStorageDriver);
      sb.OptionIfExists("--engine-env ", EngineEnv?.ToArray());
      sb.OptionIfExists("--engine-insecure-registry ", EngineInsecureRegistry?.ToArray());
      sb.OptionIfExists("--engine-registry-mirror ", EngineRegistryMirror?.ToArray());
      sb.OptionIfExists("--engine-opt ", EngineOpts?.ToArray());
      if (Swarm)
        sb.Append(" --swarm");
      sb.OptionIfExists("--tls-san ", TlsSan?.ToArray());

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine rm command.
  /// </summary>
  public struct MachineDeleteCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Force removal.</summary>
    public bool Force { get; set; }
    /// <summary>Assume yes when prompted.</summary>
    public bool Yes { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Yes)
        sb.Append(" -y");
      if (Force)
        sb.Append(" -f");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine ls command.
  /// </summary>
  public struct MachineLsCommandArgs
  {
    /// <summary>Pretty-print machines using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Filter output based on conditions provided.</summary>
    public string[] Filters { get; set; }
    /// <summary>Only print machine names.</summary>
    public bool Quiet { get; set; }
    /// <summary>Timeout in seconds.</summary>
    public int? Timeout { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      sb.OptionIfExists("--filter ", Filters);
      if (Quiet)
        sb.Append(" -q");
      if (Timeout.HasValue)
        sb.Append($" -t {Timeout.Value}");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine ssh command.
  /// </summary>
  public struct MachineSshCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Command to execute.</summary>
    public string Command { get; set; }
    /// <summary>Command arguments.</summary>
    public IList<string> Arguments { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.Append(Name);
      if (!string.IsNullOrEmpty(Command))
      {
        sb.Append($" {Command}");
        if (Arguments != null)
        {
          foreach (var arg in Arguments)
          {
            sb.Append($" {arg}");
          }
        }
      }

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine scp command.
  /// </summary>
  public struct MachineScpCommandArgs
  {
    /// <summary>Source path (machine:path or local path).</summary>
    public string Source { get; set; }
    /// <summary>Destination path (machine:path or local path).</summary>
    public string Destination { get; set; }
    /// <summary>Recursive copy.</summary>
    public bool Recursive { get; set; }
    /// <summary>Use delta transfers to reduce data transferred.</summary>
    public bool Delta { get; set; }
    /// <summary>Suppress progress output.</summary>
    public bool Quiet { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Recursive)
        sb.Append(" -r");
      if (Delta)
        sb.Append(" -d");
      if (Quiet)
        sb.Append(" -q");
      sb.Append($" {Source} {Destination}");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine inspect command.
  /// </summary>
  public struct MachineInspectCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Format the output using the given Go template.</summary>
    public string Format { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine regenerate-certs command.
  /// </summary>
  public struct MachineRegenerateCertsCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Force regeneration.</summary>
    public bool Force { get; set; }
    /// <summary>Only show client bundles path.</summary>
    public bool ClientCerts { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" -f");
      if (ClientCerts)
        sb.Append(" --client-certs");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker-machine upgrade command.
  /// </summary>
  public struct MachineUpgradeCommandArgs
  {
    /// <summary>Machine names to upgrade.</summary>
    public IList<string> Names { get; set; }
  }

  /// <summary>
  /// Arguments for docker-machine provision command.
  /// </summary>
  public struct MachineProvisionCommandArgs
  {
    /// <summary>Machine names to provision.</summary>
    public IList<string> Names { get; set; }
  }

  /// <summary>
  /// Arguments for docker-machine env command.
  /// </summary>
  public struct MachineEnvCommandArgs
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }
    /// <summary>Display the commands to set up the environment for the Docker client.</summary>
    public string Shell { get; set; }
    /// <summary>Print machine's IP address.</summary>
    public bool NoProxy { get; set; }
    /// <summary>Unset variables instead of setting them.</summary>
    public bool Unset { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--shell ", Shell);
      if (NoProxy)
        sb.Append(" --no-proxy");
      if (Unset)
        sb.Append(" --unset");

      return sb.ToString();
    }
  }
}

