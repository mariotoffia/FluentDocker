using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Executors;
using static Ductus.FluentDocker.Common.FdOs;

namespace Ductus.FluentDocker.Extensions.Utils
{
  /// <summary>
  ///   Resolves the available container engine commands on the local machine.
  ///   Supports Docker, Podman, and other compatible container engines.
  /// </summary>
  public sealed class DockerBinariesResolver
  {
    public DockerBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
      : this(sudo, password, ContainerEngine.Auto, paths)
    {
    }

    public DockerBinariesResolver(SudoMechanism sudo, string password, ContainerEngine preferredEngine, params string[] paths)
    {
      PreferredEngine = preferredEngine;
      Binaries = ResolveFromPaths(sudo, password, preferredEngine, paths).ToArray();
      
      // Select the main client based on preferred engine
      MainDockerClient = SelectMainClient(preferredEngine);
      MainDockerCompose = SelectMainCompose(preferredEngine);
      MainDockerComposeV2 = CheckComposeV2(sudo, password);
      MainDockerMachine = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Machine);
      MainDockerCli = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Cli);
      HasToolbox = Binaries.Any(x => x.IsToolbox);
      
      // Determine the actual engine being used
      ActiveEngine = MainDockerClient?.Engine ?? ContainerEngine.Docker;

      if (null == MainDockerClient)
      {
        var engineName = GetEngineName(preferredEngine);
        Logger.Log($"Failed to find {engineName} client binary - please add it to your path");
        throw new FluentDockerException($"Failed to find {engineName} client binary - please add it to your path");
      }

      if (null == MainDockerCompose && null == MainDockerComposeV2)
      {
        var engineName = GetEngineName(preferredEngine);
        Logger.Log($"Failed to find {engineName}-compose client binary (neither V1 nor V2) - please add it to your path");
      }
    }

    private DockerBinary SelectMainClient(ContainerEngine preferredEngine)
    {
      var clients = Binaries.Where(x => !x.IsToolbox && x.Type == DockerBinaryType.DockerClient).ToList();
      
      var selected = preferredEngine switch
      {
        ContainerEngine.Docker => clients.FirstOrDefault(x => x.Engine == ContainerEngine.Docker) ?? clients.FirstOrDefault(),
        ContainerEngine.Podman => clients.FirstOrDefault(x => x.Engine == ContainerEngine.Podman) ?? clients.FirstOrDefault(),
        _ => clients.FirstOrDefault(x => x.Engine == ContainerEngine.Docker) ?? clients.FirstOrDefault() // Auto: prefer Docker
      };
      
      // Log warning if preferred engine was explicitly requested but not available
      if (selected != null && preferredEngine != ContainerEngine.Auto && selected.Engine != preferredEngine)
      {
        Logger.Log($"Warning: {GetEngineName(preferredEngine)} was requested but not available. Using {GetEngineName(selected.Engine)} instead.");
      }
      
      return selected;
    }

    private DockerBinary SelectMainCompose(ContainerEngine preferredEngine)
    {
      var composes = Binaries.Where(x => !x.IsToolbox && x.Type == DockerBinaryType.Compose).ToList();
      
      var selected = preferredEngine switch
      {
        ContainerEngine.Docker => composes.FirstOrDefault(x => x.Engine == ContainerEngine.Docker) ?? composes.FirstOrDefault(),
        ContainerEngine.Podman => composes.FirstOrDefault(x => x.Engine == ContainerEngine.Podman) ?? composes.FirstOrDefault(),
        _ => composes.FirstOrDefault(x => x.Engine == ContainerEngine.Docker) ?? composes.FirstOrDefault() // Auto: prefer Docker
      };
      
      // Log warning if preferred engine was explicitly requested but not available
      if (selected != null && preferredEngine != ContainerEngine.Auto && selected.Engine != preferredEngine)
      {
        Logger.Log($"Warning: {GetEngineName(preferredEngine)}-compose was requested but not available. Using {GetEngineName(selected.Engine)}-compose instead.");
      }
      
      return selected;
    }

    private static string GetEngineName(ContainerEngine engine)
    {
      return engine switch
      {
        ContainerEngine.Docker => "docker",
        ContainerEngine.Podman => "podman",
        _ => "container engine (docker/podman)"
      };
    }

    public DockerBinary[] Binaries { get; }
    public DockerBinary MainDockerClient { get; }
    public DockerBinary MainDockerCompose { get; }
    public DockerBinary MainDockerComposeV2 { get; }
    public DockerBinary MainDockerMachine { get; }
    public DockerBinary MainDockerCli { get; }
    public bool IsDockerMachineAvailable => null != MainDockerMachine;
    public bool IsDockerComposeV2Available => null != MainDockerComposeV2;
    public bool HasToolbox { get; }
    
    /// <summary>
    /// Gets the preferred container engine specified during construction.
    /// </summary>
    public ContainerEngine PreferredEngine { get; }
    
    /// <summary>
    /// Gets the actual container engine that is being used.
    /// </summary>
    public ContainerEngine ActiveEngine { get; }

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

      var resolved = type switch
      {
        DockerBinaryType.Compose => MainDockerComposeV2 ?? MainDockerCompose, // Prefer V2 if available
        DockerBinaryType.DockerClient => MainDockerClient,
        DockerBinaryType.Machine => MainDockerMachine,
        DockerBinaryType.Cli => MainDockerCli,
        _ => throw new FluentDockerException($"Cannot resolve unknown binary {binary}"),
      };
      
      if (null == resolved)
      {
        throw new FluentDockerException($"Could not resolve binary {binary} is it installed on the local system?");
      }

      return resolved;
    }

    private static IEnumerable<DockerBinary> ResolveFromPaths(SudoMechanism sudo, string password, ContainerEngine preferredEngine, params string[] paths)
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
            // Search for Docker binaries
            if (preferredEngine == ContainerEngine.Auto || preferredEngine == ContainerEngine.Docker)
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
            }

            // Search for Podman binaries
            if (preferredEngine == ContainerEngine.Auto || preferredEngine == ContainerEngine.Podman)
            {
              list.AddRange(from file in Directory.GetFiles(path, "podman*.*")
                            let f = Path.GetFileName(file.ToLower())
                            where null != f && (f.Equals("podman.exe") || f.Equals("podman-compose.exe"))
                            select new DockerBinary(path, f, sudo, password));
            }

            continue;
          }

          // Linux/macOS: Search for Docker binaries
          if (preferredEngine == ContainerEngine.Auto || preferredEngine == ContainerEngine.Docker)
          {
            list.AddRange(from file in Directory.GetFiles(path, "docker*")
                          let f = Path.GetFileName(file)
                          let f2 = f.ToLower()
                          where f2.Equals("docker") || f2.Equals("docker-compose") || f2.Equals("docker-machine")
                          select new DockerBinary(path, f, sudo, password));
          }

          // Linux/macOS: Search for Podman binaries
          if (preferredEngine == ContainerEngine.Auto || preferredEngine == ContainerEngine.Podman)
          {
            list.AddRange(from file in Directory.GetFiles(path, "podman*")
                          let f = Path.GetFileName(file)
                          let f2 = f.ToLower()
                          where f2.Equals("podman") || f2.Equals("podman-compose")
                          select new DockerBinary(path, f, sudo, password));
          }
        }
        catch (Exception e)
        {
          // Illegal character in path if env variable PATH like this (spaces before ';'):
          // c:\folder1;c:\folder2;c:\folder3     ;
          Logger.Log("Failed to get container engine binary from path: " + path + Environment.NewLine + e);
        }
      }
      return list;
    }

    private DockerBinary CheckComposeV2(SudoMechanism sudo, string password)
    {
      if (null == MainDockerClient)
        return null;

      try
      {
        // Test if 'compose' subcommand exists (V2 style - both Docker and Podman support this)
        var result = new ProcessExecutor<Executors.Parsers.StringListResponseParser, IList<string>>(
          MainDockerClient.FqPath,
          "compose version").Execute();

        if (result.Success)
        {
          // Compose V2 is available
          return new DockerBinary(
            System.IO.Path.GetDirectoryName(MainDockerClient.FqPath), 
            System.IO.Path.GetFileName(MainDockerClient.FqPath), 
            sudo, 
            password, 
            DockerBinaryType.ComposeV2);
        }
      }
      catch
      {
        // Compose V2 plugin is not available
        Logger.Log("Compose V2 plugin is not available");
      }
      
      return null;
    }
  }
}
