using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiImageDriver
  {
    private static async Task WriteStreamAtomicallyAsync(
        Stream stream, string outputPath, CancellationToken cancellationToken)
    {
      var fullPath = Path.GetFullPath(outputPath);
      var directory = Path.GetDirectoryName(fullPath);
      if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

      var tempPath = Path.Combine(directory ?? ".",
          $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
      try
      {
        await using (var fileStream = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous))
        {
          await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, fullPath, overwrite: true);
      }
      catch
      {
        try
        {
          File.Delete(tempPath);
        }
        catch
        {
        }
        throw;
      }
    }
  }
}
