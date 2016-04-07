using System;
using Ductus.FluentDocker.Internal;

namespace Ductus.FluentDocker.Extensions
{
  public static class DockerBinaryUtils
  {
    public static string DockerPath(this string dockerCommand)
    {
      dockerCommand = dockerCommand.ToLower();
      switch (dockerCommand)
      {
        case "docker-machine":
          return HasMachine()
            ? "${E_DOCKER_TOOLBOX_INSTALL_PATH}/docker-machine".Render().ToPlatformPath()
            : dockerCommand;
        case "docker":
          return HasMachine()
            ? "${E_DOCKER_TOOLBOX_INSTALL_PATH}/docker".Render().ToPlatformPath()
            : dockerCommand;
      }

      throw new ArgumentException($"No command with name {dockerCommand} is present");
    }

    private static bool HasMachine()
    {
      return null != Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH");
    }
  }
}
