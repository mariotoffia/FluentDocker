using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Ductus.FluentDocker.Extensions
{
  internal static class DockerClientExtensions
  {
    private const string DebugCategory = "Npsql.Docker";

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
          Debugger.Log((int) TraceLevel.Verbose, DebugCategory, line);
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
        throw new FluentDockerException($"Failed to start container {id}",e);
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
  }
}