using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Docker.DotNet;
using Docker.DotNet.Models;
using Ductus.FluentDocker.Internal;
using SharpCompress.Common;
using SharpCompress.Reader;

namespace Ductus.FluentDocker.Extensions
{
  internal static class StreamExtensions
  {
    internal static string ExportContainer(this DockerClient client, string id, string hostFilePath,
      bool explode = false)
    {
      var tmp = Path.GetTempFileName();

      try
      {
        hostFilePath = hostFilePath.Render().ToPlatformPath();
        using (var stream =
          client.Containers.ExportContainerAsync(id, CancellationToken.None).Result)
        {
          CopyStream(stream, tmp);
        }

        if (!explode)
        {
          return tmp;
        }

        Extract(tmp, hostFilePath);
        return hostFilePath;
      }
      catch (Exception)
      {
        return null;
      }
      finally
      {
        if (explode)
        {
          DeleteFile(tmp);
        }
      }
    }

    internal static string CopyFromContainer(this DockerClient client, string id, string containerFilePath,
      string hostFilePath)
    {
      var tmp = Path.GetTempFileName();

      try
      {
        hostFilePath = hostFilePath.Render().ToPlatformPath();

        using (var stream =
          client.Containers.CopyFromContainerAsync(id,
            new CopyFromContainerParameters {Resource = containerFilePath}, CancellationToken.None)
            .Result)
        {
          CopyStream(stream, tmp);
        }

        Extract(tmp, hostFilePath);
        return hostFilePath;
      }
      catch (Exception)
      {
        return null;
      }
      finally
      {
        DeleteFile(tmp);
      }
    }

    private static void Extract(string file, string destPath)
    {
      using (var stream = File.OpenRead(file))
      {
        using (var reader = ReaderFactory.Open(stream))
        {
          while (reader.MoveToNextEntry())
          {
            if (!reader.Entry.IsDirectory)
            {
              reader.WriteEntryToDirectory(destPath, ExtractOptions.ExtractFullPath | ExtractOptions.Overwrite);
            }
          }
        }
      }
    } 
    private static void CopyStream(Stream stream, string destFile)
    {
      using (var writer = File.OpenWrite(destFile))
      {
        stream.CopyTo(writer, 4096);
      }
    }

    private static void DeleteFile(string file)
    {
      try
      {
        File.Delete(file);
      }
      catch (Exception)
      {
        // Ignore
      }
    }
  }
}