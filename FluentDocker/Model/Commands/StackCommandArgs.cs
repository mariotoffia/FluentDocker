using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Stacks;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker stack ls command.
  /// </summary>
  public struct StackLsCommandArgs
  {
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>List stacks from all Kubernetes namespaces.</summary>
    public bool AllNamespaces { get; set; }
    /// <summary>Kubernetes namespace to use.</summary>
    public string Namespace { get; set; }
    /// <summary>Path to Kubernetes config file.</summary>
    public string KubeConfigFile { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      if (AllNamespaces)
        sb.Append(" --all-namespaces");
      sb.OptionIfExists("--namespace=", Namespace);
      sb.OptionIfExists("--kubeconfig=", KubeConfigFile);
      sb.OptionIfExists("--format ", Format);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stack ps command.
  /// </summary>
  public struct StackPsCommandArgs
  {
    /// <summary>Stack name.</summary>
    public string Stack { get; set; }
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>Kubernetes namespace to use.</summary>
    public string Namespace { get; set; }
    /// <summary>Path to Kubernetes config file.</summary>
    public string KubeConfigFile { get; set; }
    /// <summary>Filter output based on conditions.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Do not map IDs to Names.</summary>
    public bool NoResolve { get; set; }
    /// <summary>Only display task IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      sb.OptionIfExists("--namespace=", Namespace);
      sb.OptionIfExists("--kubeconfig=", KubeConfigFile);
      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (NoTrunc)
        sb.Append(" --no-trunc");
      if (NoResolve)
        sb.Append(" --no-resolve");
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stack rm command.
  /// </summary>
  public struct StackRmCommandArgs
  {
    /// <summary>Stack names to remove.</summary>
    public IList<string> Stacks { get; set; }
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>Kubernetes namespace to use.</summary>
    public string Namespace { get; set; }
    /// <summary>Path to Kubernetes config file.</summary>
    public string KubeConfigFile { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      sb.OptionIfExists("--namespace=", Namespace);
      sb.OptionIfExists("--kubeconfig=", KubeConfigFile);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stack deploy command.
  /// </summary>
  public struct StackDeployCommandArgs
  {
    /// <summary>Stack name.</summary>
    public string Stack { get; set; }
    /// <summary>Path to a Compose file, or "-" to read from stdin.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>Kubernetes namespace to use.</summary>
    public string Namespace { get; set; }
    /// <summary>Path to Kubernetes config file.</summary>
    public string KubeConfigFile { get; set; }
    /// <summary>Prune services that are no longer referenced.</summary>
    public bool Prune { get; set; }
    /// <summary>Query the registry to resolve image digest and supported platforms.</summary>
    public bool ResolveImage { get; set; }
    /// <summary>Send registry authentication details to Swarm agents.</summary>
    public bool WithRegistryAuth { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (ComposeFiles != null)
      {
        foreach (var file in ComposeFiles)
        {
          sb.Append($" -c \"{file}\"");
        }
      }

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      sb.OptionIfExists("--namespace=", Namespace);
      sb.OptionIfExists("--kubeconfig=", KubeConfigFile);
      if (Prune)
        sb.Append(" --prune");
      if (ResolveImage)
        sb.Append(" --resolve-image always");
      if (WithRegistryAuth)
        sb.Append(" --with-registry-auth");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stack services command.
  /// </summary>
  public struct StackServicesCommandArgs
  {
    /// <summary>Stack name.</summary>
    public string Stack { get; set; }
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>Kubernetes namespace to use.</summary>
    public string Namespace { get; set; }
    /// <summary>Path to Kubernetes config file.</summary>
    public string KubeConfigFile { get; set; }
    /// <summary>Filter output based on conditions.</summary>
    public string[] Filters { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Only display IDs.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      sb.OptionIfExists("--namespace=", Namespace);
      sb.OptionIfExists("--kubeconfig=", KubeConfigFile);
      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);
      if (Quiet)
        sb.Append(" --quiet");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stack config command.
  /// </summary>
  public struct StackConfigCommandArgs
  {
    /// <summary>Path to a Compose file.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Orchestrator to use.</summary>
    public Orchestrator Orchestrator { get; set; }
    /// <summary>Skip interpolation and output only merged config.</summary>
    public bool SkipInterpolation { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (ComposeFiles != null)
      {
        foreach (var file in ComposeFiles)
        {
          sb.Append($" -c \"{file}\"");
        }
      }

      if (Orchestrator != Orchestrator.All)
        sb.Append($" --orchestrator={Orchestrator}");
      if (SkipInterpolation)
        sb.Append(" --skip-interpolation");

      return sb.ToString();
    }
  }
}

