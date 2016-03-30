using Docker.DotNet.Models;

namespace Ductus.FluentDocker.Extensions
{
  internal static class SettingsExtensions
  {
    internal static int GetHostPort(this ContainerResponse settings, string containerPort)
    {
      var portBindings = settings.NetworkSettings.Ports;
      if (!portBindings.ContainsKey(containerPort))
      {
        return -1;
      }

      var portBinding = portBindings[containerPort];
      if (0 == portBinding.Count)
      {
        return -1;
      }

      return int.Parse(portBinding[0].HostPort);
    }
  }
}
