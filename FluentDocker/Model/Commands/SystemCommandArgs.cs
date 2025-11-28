using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker system df command.
  /// </summary>
  public struct SystemDfCommandArgs
  {
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Show detailed information on space usage.</summary>
    public bool Verbose { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Verbose)
        sb.Append(" --verbose");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker system prune command.
  /// </summary>
  public struct SystemPruneCommandArgs
  {
    /// <summary>Remove all unused images not just dangling ones.</summary>
    public bool All { get; set; }
    /// <summary>Provide filter values.</summary>
    public string[] Filters { get; set; }
    /// <summary>Do not prompt for confirmation.</summary>
    public bool Force { get; set; }
    /// <summary>Prune volumes.</summary>
    public bool Volumes { get; set; }
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
      if (Volumes)
        sb.Append(" --volumes");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker system info command.
  /// </summary>
  public struct SystemInfoCommandArgs
  {
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
  /// Arguments for docker version command.
  /// </summary>
  public struct VersionCommandArgs
  {
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Kubernetes config file.</summary>
    public string KubeConfig { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      sb.OptionIfExists("--kubeconfig ", KubeConfig);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker context ls command.
  /// </summary>
  public struct ContextLsCommandArgs
  {
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Only show context names.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker context use command.
  /// </summary>
  public struct ContextUseCommandArgs
  {
    /// <summary>Context name.</summary>
    public string Name { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }
  }

  /// <summary>
  /// Arguments for docker context create command.
  /// </summary>
  public struct ContextCreateCommandArgs
  {
    /// <summary>Context name.</summary>
    public string Name { get; set; }
    /// <summary>Description of the context.</summary>
    public string Description { get; set; }
    /// <summary>Docker endpoint (format: host=DOCKER_HOST).</summary>
    public string Docker { get; set; }
    /// <summary>Context to use as base.</summary>
    public string From { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--description ", Description);
      sb.OptionIfExists("--docker ", Docker);
      sb.OptionIfExists("--from ", From);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker context rm command.
  /// </summary>
  public struct ContextRmCommandArgs
  {
    /// <summary>Context names to remove.</summary>
    public string[] Names { get; set; }
    /// <summary>Force removal.</summary>
    public bool Force { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Force)
        sb.Append(" --force");

      return sb.ToString();
    }
  }
}

