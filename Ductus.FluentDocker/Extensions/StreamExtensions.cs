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
          using (var writer = File.OpenWrite(tmp))
          {
            stream.CopyTo(writer, 4096);
          }
        }

        using (var stream = File.OpenRead(tmp))
        {
          using (var reader = ReaderFactory.Open(stream))
          {
            while (reader.MoveToNextEntry())
            {
              if (!reader.Entry.IsDirectory)
              {
                reader.WriteEntryToDirectory(hostFilePath, ExtractOptions.ExtractFullPath | ExtractOptions.Overwrite);
              }
            }
          }
        }

        return hostFilePath;
      }
      catch (Exception e)
      {
        Debug.WriteLine(e.Message);
        return null;
      }
      finally
      {
        try
        {
          File.Delete(tmp);
        }
        catch (Exception)
        {
          // Ignore
        }
      }
    }
  }
}