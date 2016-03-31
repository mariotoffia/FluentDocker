using System.Collections.Generic;
using System.Linq;
using Docker.DotNet.Models;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker.Extensions
{
  internal static class DockerParamsTranslations
  {
    internal static HostConfig ToHostConfig(this DockerParams prms)
    {
      IDictionary<string, IList<PortBinding>> ports = null;
      if (null != prms.Ports && 0 != prms.Ports.Length)
      {
        ports = prms.Ports.ToDictionary<string, string, IList<PortBinding>>(port => port,
          port => new List<PortBinding> { new PortBinding { HostIp = "0.0.0.0", HostPort = "" } });
      }

      return new HostConfig
      {
        PortBindings = ports,
        Binds = DockerVolumeMount.ToStringArray(prms.Volumes),
        Links = prms.Links.Count == 0 ? null : prms.Links
      };
    }

    internal static CreateContainerParameters ToParameters(this DockerParams prms)
    {
      IDictionary<string, object> ports = null;
      if (null != prms.Ports && 0 != prms.Ports.Length)
      {
        ports = prms.Ports.ToDictionary<string, string, object>(port => port,
          port => new PortBinding { HostIp = "0.0.0.0", HostPort = "" });
      }

      IDictionary<string, object> vols = null;
      if (null != prms.Volumes && 0 != prms.Volumes.Length)
      {
        vols = new Dictionary<string, object>();
        foreach (var mount in prms.Volumes)
        {
          vols.Add(mount.Docker, new object());
        }
      }

      return new CreateContainerParameters
      {
        ContainerName = prms.ContainerName,
        Config = new Config
        {
          Env = prms.Env,
          Image = prms.ImageName,
          Cmd = null == prms.Cmd ? null : new List<string>(prms.Cmd),
          DomainName = prms.DomainName,
          User = prms.User,
          Hostname = prms.HostName,
          NetworkDisabled = false,
          ExposedPorts = ports,
          Volumes = vols
        }
      };
    }

  }
}
