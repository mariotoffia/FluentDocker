using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class with build-context helpers: tar packaging (streamed to a temp file,
  /// honouring <c>.dockerignore</c>) and the <c>/build</c> query-string assembly.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    #region Build Context

    /// <summary>
    /// Packs the build context directory into a tar archive, streamed to a temp file
    /// (deleted on close) rather than buffered in memory, and filtered through the
    /// context's <c>.dockerignore</c> rules. The caller owns the returned stream.
    /// </summary>
    private static Stream CreateBuildContextTar(string contextPath, ImageBuildConfig config)
    {
      var dockerfileName = string.IsNullOrEmpty(config?.DockerfileName)
          ? "Dockerfile"
          : config.DockerfileName;
      var filter = DockerIgnoreFilter.Load(contextPath, dockerfileName);

      // Temp file with DeleteOnClose: bounds memory regardless of context size and
      // auto-deletes when the caller disposes the stream.
      var tempPath = Path.GetTempFileName();
      var fileStream = new FileStream(
          tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
          bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
      try
      {
        using (var writer = SharpCompress.Writers.WriterFactory.OpenWriter(
            fileStream, SharpCompress.Common.ArchiveType.Tar,
            new SharpCompress.Writers.Tar.TarWriterOptions(
                SharpCompress.Common.CompressionType.None, true)))
        {
          var contextDir = new DirectoryInfo(contextPath);
          foreach (var file in contextDir.GetFiles("*", SearchOption.AllDirectories))
          {
            var relativePath = Path.GetRelativePath(contextPath, file.FullName)
                .Replace('\\', '/');
            if (filter.IsIgnored(relativePath))
              continue;
            using var src = file.OpenRead();
            writer.Write(relativePath, src, file.LastWriteTimeUtc);
          }
        }

        fileStream.Position = 0;
        return fileStream;
      }
      catch
      {
        fileStream.Dispose();
        throw;
      }
    }

    private static List<string> BuildBuildQueryParams(ImageBuildConfig config)
    {
      var query = new List<string>();

      if (!string.IsNullOrEmpty(config.DockerfileName))
        query.Add($"dockerfile={Uri.EscapeDataString(config.DockerfileName)}");

      foreach (var tag in config.Tags ?? Enumerable.Empty<string>())
        query.Add($"t={Uri.EscapeDataString(tag)}");

      if (config.NoCache)
        query.Add("nocache=true");

      if (config.Pull)
        query.Add("pull=true");

      if (!config.Rm)
        query.Add("rm=false");

      if (config.ForceRm)
        query.Add("forcerm=true");

      if (config.Squash)
        query.Add("squash=true");

      if (!string.IsNullOrEmpty(config.Target))
        query.Add($"target={Uri.EscapeDataString(config.Target)}");

      if (!string.IsNullOrEmpty(config.Platform))
        query.Add($"platform={Uri.EscapeDataString(config.Platform)}");

      if (!string.IsNullOrEmpty(config.NetworkMode))
        query.Add($"networkmode={Uri.EscapeDataString(config.NetworkMode)}");

      if (config.Memory.HasValue)
        query.Add($"memory={config.Memory.Value}");

      if (config.CpuQuota.HasValue)
        query.Add($"cpuquota={config.CpuQuota.Value}");

      if (config.BuildArgs?.Count > 0)
      {
        var json = JsonHelper.Serialize(config.BuildArgs);
        query.Add($"buildargs={Uri.EscapeDataString(json)}");
      }

      if (config.Labels?.Count > 0)
      {
        var json = JsonHelper.Serialize(config.Labels);
        query.Add($"labels={Uri.EscapeDataString(json)}");
      }

      return query;
    }

    #endregion
  }
}
