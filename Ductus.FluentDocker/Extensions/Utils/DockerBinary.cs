using System;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions.Utils
{
  public enum DockerBinaryType
  {
    DockerClient = 1,
    Machine = 2,
    Compose = 3,
    Cli = 4,
    // ComposeV2 is a `DockerClient` that do support the subcommand `compose`
    // This is used to distinguish between the old `docker-compose` and the new `docker compose` command.    
    ComposeV2 = 5
  }

  public sealed class DockerBinary
  {
    internal DockerBinary(string path, string binary, SudoMechanism sudo, string password)
    {
      Path = path;
      Binary = binary.ToLower();
      Type = Translate(binary);
      Sudo = sudo;
      SudoPassword = password;
      Engine = DetermineEngine(binary);

      var isToolbox = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.Equals(Path);
      IsToolbox = isToolbox ?? false;
    }

    internal DockerBinary(string path, string binary, SudoMechanism sudo, string password, DockerBinaryType type)
    {
      Path = path;
      Binary = binary.ToLower();
      Type = type;
      Sudo = sudo;
      SudoPassword = password;
      Engine = DetermineEngine(binary);

      var isToolbox = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.Equals(Path);
      IsToolbox = isToolbox ?? false;
    }

    public static DockerBinaryType Translate(string binary)
    {
      switch (binary.ToLower())
      {
        case "docker":
        case "docker.exe":
        case "podman":
        case "podman.exe":
          return DockerBinaryType.DockerClient;
        case "docker-machine":
        case "docker-machine.exe":
          return DockerBinaryType.Machine;
        case "docker-compose":
        case "docker-compose.exe":
        case "podman-compose":
        case "podman-compose.exe":
          return DockerBinaryType.Compose;
        case "dockercli":
        case "dockercli.exe":
          return DockerBinaryType.Cli;
        default:
          throw new Exception($"Cannot determine the container engine type for binary {binary}");
      }
    }

    /// <summary>
    /// Determines the container engine based on the binary name.
    /// </summary>
    private static ContainerEngine DetermineEngine(string binary)
    {
      var lowerBinary = binary.ToLower();
      if (lowerBinary.StartsWith("podman"))
        return ContainerEngine.Podman;
      return ContainerEngine.Docker;
    }

    public string FqPath => System.IO.Path.Combine(Path, Binary);
    public string Path { get; }
    public string Binary { get; }
    public DockerBinaryType Type { get; }
    public bool IsToolbox { get; }
    public SudoMechanism Sudo { get; }
    public string SudoPassword { get; }
    
    /// <summary>
    /// Gets the container engine this binary belongs to.
    /// </summary>
    public ContainerEngine Engine { get; }
  }
}
