using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker volume create command.
  /// </summary>
  public struct VolumeCreateCommandArgs
  {
    /// <summary>Specify volume name.</summary>
    public string Name { get; set; }
    /// <summary>Specify volume driver name.</summary>
    public string Driver { get; set; }
    /// <summary>Set metadata for a volume.</summary>
    public string[] Labels { get; set; }
    /// <summary>Set driver specific options.</summary>
    public IDictionary<string, string> DriverOpts { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--name=", Name);
      sb.OptionIfExists("--driver=", Driver);
      sb.OptionIfExists("--label=", Labels);
      sb.OptionIfExists("--opt=", DriverOpts);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker volume ls command.
  /// </summary>
  public struct VolumeLsCommandArgs
  {
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Only display volume names.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" -q");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker volume rm command.
  /// </summary>
  public struct VolumeRmCommandArgs
  {
    /// <summary>Volume names to remove.</summary>
    public IList<string> Volumes { get; set; }
    /// <summary>Force the removal of one or more volumes.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" -f");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker volume inspect command.
  /// </summary>
  public struct VolumeInspectCommandArgs
  {
    /// <summary>Volume names to inspect.</summary>
    public IList<string> Volumes { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker volume prune command.
  /// </summary>
  public struct VolumePruneCommandArgs
  {
    /// <summary>Remove all unused volumes, not just anonymous ones.</summary>
    public bool All { get; set; }
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Do not prompt for confirmation.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      sb.OptionIfExists("--filter ", Filters);
      if (Force)
        sb.Append(" --force");

      return sb.ToString();
    }
  }
}

