using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using Ductus.FluentDocker.Internal;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Extensions
{
  internal static class DockerClientExtensions
  {
    internal static CreateContainerResponse CreateContainer(this DockerClient client, CreateContainerParameters prms,
      bool pullIfMissing)
    {
      if (!ContainerExists(client, prms.Config.Image))
      {
        if (!pullIfMissing)
        {
          return null;
        }

        if (!PullImage(client, prms.Config.Image))
        {
          return null;
        }
      }

      try
      {
        return client.Containers.CreateContainerAsync(prms).Result;
      }
      catch (Exception)
      {
        // Ignore
      }

      return null;
    }

    internal static bool PullImage(this DockerClient client, string image)
    {
      var stream = client.Images.CreateImageAsync(new CreateImageParameters
      {
        FromImage = image
      }, null).Result;

      using (var strm = new StreamReader(new BufferedStream(stream)))
      {
        while (!strm.EndOfStream)
        {
          var line = strm.ReadLine();
          Debugger.Log((int) TraceLevel.Verbose, Constants.DebugCategory, line);
        }
      }

      return true;
    }

    internal static bool ContainerExists(this DockerClient client, string image)
    {
      var result = client.Images.ListImagesAsync(new ListImagesParameters
      {
        All = true
      }).Result;


      return result.Any(img => img.RepoTags.Any(tag => tag == image));
    }

    internal static void StartContainer(this DockerClient client, string id, HostConfig config)
    {
      try
      {
        if (!client.Containers.StartContainerAsync(id, config).Result)
        {
          throw new FluentDockerException($"Failed to start container {id}");
        }
      }
      catch (Exception e)
      {
        throw new FluentDockerException($"Failed to start container {id}", e);
      }
    }

    internal static bool StopContainer(this DockerClient client, string id)
    {
      try
      {
        client.Containers.StopContainerAsync(id,
          new StopContainerParameters {Wait = TimeSpan.FromSeconds(10)}, CancellationToken.None).Wait();
      }
      catch (Exception)
      {
        return false;
      }

      return true;
    }

    internal static bool RemoveContainer(this DockerClient client, string id)
    {
      try
      {
        client.Containers.RemoveContainerAsync(id, new RemoveContainerParameters
        {
          Force = true,
          RemoveVolumes = true
        }).Wait();
      }
      catch (Exception)
      {
        return false;
      }

      return true;
    }

    internal static Processes ContainerProcesses(this DockerClient client, string id, string args)
    {
      try
      {
        var res =
          client.Containers.ListProcessesAsync(id, new ListProcessesParameters {PsArgs = args}).Result;

        var processes = new Processes {Columns = res.Titles, Rows = new List<ProcessRow>()};
        foreach (var row in res.Processes)
        {
          processes.Rows.Add(ProcessRow.ToRow(res.Titles, row));
        }

        return processes;
      }
      catch (Exception)
      {
        return new Processes();
      }
    }
  }
}