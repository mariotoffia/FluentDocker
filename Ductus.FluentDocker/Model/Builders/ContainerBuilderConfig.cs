using System;
using System.Collections.Generic;
using System.Net;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class DestroyIfExistParams {
    public bool Force { get; set; }
    public bool RemoveVolumes { get; set; }
    public string LinkToRemove { get; set; }
  }

  public sealed class ContainerBuilderConfig
  {
    public ContainerBuilderConfig() => CreateParams = new ContainerCreateParams();

    /// <summary>
    /// When set to true, the container will be removed if it exists before creating it.
    /// </summary>
    /// <value>true if it shall be destroyed if exists, false if skip checking and destroy.</value>
    /// <remarks>
    /// Default is false.
    /// </remarks>
    public DestroyIfExistParams DestroyIfExists { get; set; }
    public bool VerifyExistence { get; set; }
    public ContainerCreateParams CreateParams { get; }
    public string Image { get; set; }
    [Obsolete("Please use the properly spelled `ImageForcePull` method instead.")]
    public bool ImageFocrePull
    {
      get => ImageForcePull;
      set => ImageForcePull = value;
    }
    public bool ImageForcePull { get; set; }
    public bool IsWindowsImage { get; set; }
    public bool StopOnDispose { get; set; } = true;
    public bool DeleteOnDispose { get; set; } = true;
    public bool DeleteVolumeOnDispose { get; set; } = false;
    public bool DeleteNamedVolumeOnDispose { get; set; } = false;
    public Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> CustomResolver { get; set; }
    public string Command { get; set; }
    public string[] Arguments { get; set; }
    public Tuple<string /*portAndProto*/, string /*address*/ , long /*waitTimeout*/> WaitForPort { get; set; }
    public Tuple<long /*waitTimeout*/> WaitForHealthy { get; set; }
    public Tuple<long/*waitTimeout*/, string /*message*/> WaitForMessageInLog { get; set; }
    public List<ContainerSpecificConfig.WaitForHttpParams> WaitForHttp { get; } =
      new List<ContainerSpecificConfig.WaitForHttpParams>();
    public List<Func<IContainerService, int, int>> WaitLambda { get; } = new List<Func<IContainerService, int, int>>();
    public Tuple<string /*process*/, long /*waitTimeout*/> WaitForProcess { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpToOnStart { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpFromOnDispose { get; set; }
    public Tuple<TemplateString /*host*/, bool /*explode*/,
      Func<IContainerService, bool> /*condition*/> ExportOnDispose
    { get; set; }
    public List<INetworkService> Networks { get; set; }
    public List<NetworkWithAlias<INetworkService>> NetworksWithAlias { get; set; }
    public List<string> NetworkNames { get; set; }
    public List<NetworkWithAlias<string>> NetworkNamesWithAlias { get; set; }
    public List<string> ExecuteOnRunningArguments { get; set; }
    public List<string> ExecuteOnDisposingArguments { get; set; }

    public NetworkWithAlias<string> FindFirstNetworkNameAndAlias()
    {

      if (Networks != null && Networks.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = Networks[0].Name,
        };
      }
      else if (NetworksWithAlias != null && NetworksWithAlias.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = NetworksWithAlias[0].Network.Name,
          Alias = NetworksWithAlias[0].Alias
        };
      }
      else if (NetworkNames != null && NetworkNames.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = NetworkNames[0],
        };
      }
      else if (NetworkNamesWithAlias != null && NetworkNamesWithAlias.Count > 0)
      {
        return new NetworkWithAlias<string>
        {
          Network = NetworkNamesWithAlias[0].Network,
          Alias = NetworkNamesWithAlias[0].Alias
        };
      }

      return new NetworkWithAlias<string>
      {
        Network = string.Empty,
        Alias = string.Empty
      };
    }
  }
}
