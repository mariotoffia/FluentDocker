using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using static Ductus.FluentDocker.Common.FdOs;

namespace Ductus.FluentDocker.Extensions.Utils
{
  /// <summary>
  ///   Resolves the available docker commands on the local machine.
  /// </summary>
  public sealed class DockerBinariesResolver
  {
    public DockerBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
    {
      Binaries = ResolveFromPaths(sudo, password, paths).ToArray();
      MainDockerClient = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.DockerClient);
      MainDockerCompose = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Compose);
      MainDockerMachine = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Machine);
      MainDockerCli = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Cli);
      HasToolbox = Binaries.Any(x => x.IsToolbox);

      if (null == MainDockerClient)
      {
        Logger.Log("Failed to find docker client binary - please add it to your path");
        throw new FluentDockerException("Failed to find docker client binary - please add it to your path");
      }

      if (null == MainDockerCompose)
      {
        Logger.Log("Failed to find docker-compose client binary - please add it to your path");
      }

      if (null == MainDockerMachine)
      {
        Logger.Log("Failed to find docker-machine client binary - please add it to your path");
      }
    }

    public DockerBinary[] Binaries { get; }
    public DockerBinary MainDockerClient { get; }
    public DockerBinary MainDockerCompose { get; }
    public DockerBinary MainDockerMachine { get; }
    public DockerBinary MainDockerCli { get; }
    public bool HasToolbox { get; }

    public DockerBinary Resolve(string binary, bool preferMachine = false)
    {
      var type = DockerBinary.Translate(binary);
      if (preferMachine)
      {
        var m = Binaries.FirstOrDefault(x => x.IsToolbox && x.Type == type);
        if (null != m)
        {
          return m;
        }
      }

      DockerBinary resolved = null;
      switch (type)
      {
        case DockerBinaryType.Compose:
          resolved = MainDockerCompose;
          break;
        case DockerBinaryType.DockerClient:
          resolved = MainDockerClient;
          break;
        case DockerBinaryType.Machine:
          resolved = MainDockerMachine;
          break;
        case DockerBinaryType.Cli:
          resolved = MainDockerCli;
          break;
        default:
          throw new FluentDockerException($"Cannot resolve unknown binary {binary}");
      }

      if (null == resolved)
      {
        throw new FluentDockerException($"Could not resolve binary {binary} is it installed on the local system?");
      }

      return resolved;
    }

    private static IEnumerable<DockerBinary> ResolveFromPaths(SudoMechanism sudo, string password, params string[] paths)
    {
      var isWindows = IsWindows();
      if (null == paths || 0 == paths.Length)
      {
        var complete = new List<string>();
        var toolboxpath = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH");
        var envpaths = Environment.GetEnvironmentVariable("PATH")?.Split(isWindows ? ';' : ':');

        if (null != envpaths)
          complete.AddRange(envpaths);
        if (null != toolboxpath)
          complete.Add(toolboxpath);

        paths = complete.ToArray();
      }

      if (null == paths)
        return new DockerBinary[0];

      var list = new List<DockerBinary>();
      foreach (var path in paths)
      {
        try
        {
          if (!Directory.Exists(path))
          {
            continue;
          }

          if (isWindows)
          {
            list.AddRange(from file in Directory.GetFiles(path, "docker*.*")
                          let f = Path.GetFileName(file.ToLower())
                          where null != f && (f.Equals("docker.exe") || f.Equals("docker-compose.exe") ||
                                              f.Equals("docker-machine.exe"))
                          select new DockerBinary(path, f, sudo, password));

            var dockercli = Path.GetFullPath(Path.Combine(path, "..\\.."));
            if (File.Exists(Path.Combine(dockercli, "dockercli.exe")))
            {
              list.Add(new DockerBinary(dockercli, "dockercli.exe", sudo, password));
            }

            continue;
          }

          list.AddRange(from file in Directory.GetFiles(path, "docker*")
                        let f = Path.GetFileName(file)
                        let f2 = f.ToLower()
                        where f2.Equals("docker") || f2.Equals("docker-compose") || f2.Equals("docker-machine")
                        select new DockerBinary(path, f, sudo, password));
        }
        catch (Exception e)
        {
          // Illegal character in path if env variable PATH like this (spaces before ';'):
          // c:\folder1;c:\folder2;c:\folder3     ;
          Logger.Log("Failed to get docker binary from path: " + path + Environment.NewLine + e);
        }
      }
      return list;
    }
  }
}
