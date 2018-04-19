using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using static Ductus.FluentDocker.Common.OperatingSystem;

namespace Ductus.FluentDocker.Extensions.Utils
{
  /// <summary>
  ///   Resolves the available docker commands on the local machine.
  /// </summary>
  public sealed class DockerBinariesResolver
  {
    public DockerBinariesResolver(params string []paths)
    {
      Binaries = ResolveFromPaths(paths).ToArray();
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

      switch (type)
      {
        case DockerBinaryType.Compose:
          return MainDockerCompose;
        case DockerBinaryType.DockerClient:
          return MainDockerClient;
        case DockerBinaryType.Machine:
          return MainDockerMachine;
        case DockerBinaryType.Cli:
          return MainDockerCli;
        default:
          throw new Exception($"Cannot resolve unknown binary {binary}");
      }
    }

    private static IEnumerable<DockerBinary> ResolveFromPaths(params string[]paths)
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
        if (!Directory.Exists(path))
        {
          continue;
        }

        if (isWindows)
        {
          list.AddRange(from file in Directory.GetFiles(path,"docker*.*")
            let f = Path.GetFileName(file.ToLower())
            where null != f && (f.Equals("docker.exe") || f.Equals("docker-compose.exe") || f.Equals("docker-machine.exe"))
            select new DockerBinary(path, f));

          var dockercli = Path.GetFullPath(Path.Combine(path, "..\\.."));
          if (File.Exists(Path.Combine(dockercli,"dockercli.exe")))
          {
            list.Add(new DockerBinary(dockercli,"dockercli.exe"));
          }

          continue;
        }

        list.AddRange(from file in Directory.GetFiles(path,"docker*")
          let f = Path.GetFileName(file)
          let f2 = f.ToLower()
          where f2.Equals("docker") || f2.Equals("docker-compose") || f2.Equals("docker-machine")
          select new DockerBinary(path, f));
      }
      return list;
    }
  }
}