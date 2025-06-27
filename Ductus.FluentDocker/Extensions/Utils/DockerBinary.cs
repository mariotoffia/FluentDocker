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

      var isToolbox = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.Equals(Path);
      IsToolbox = isToolbox ?? false;
    }

    public static DockerBinaryType Translate(string binary)
    {
      switch (binary.ToLower())
      {
        case "docker":
        case "docker.exe":
          return DockerBinaryType.DockerClient;
        case "docker-machine":
        case "docker-machine.exe":
          return DockerBinaryType.Machine;
        case "docker-compose":
        case "docker-compose.exe":
          return DockerBinaryType.Compose;
        case "dockercli":
        case "dockercli.exe":
          return DockerBinaryType.Cli;
        default:
          throw new Exception($"Cannot determine the docker type on binary {binary}");
      }
    }

    public string FqPath => System.IO.Path.Combine(Path, Binary);
    public string Path { get; }
    public string Binary { get; }
    public DockerBinaryType Type { get; }
    public bool IsToolbox { get; }
    public SudoMechanism Sudo { get; }
    public string SudoPassword { get; }
  }
}
