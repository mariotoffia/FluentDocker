#nullable enable
using System;
using System.IO;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// Legacy archive extraction helpers.
  /// </summary>
  public static class CompressionExtensions
  {
    /// <summary>
    /// Extracts a tar archive to a directory.
    /// </summary>
    [Obsolete("UnTar is a legacy test helper with broad extraction surface; prefer purpose-specific archive extraction.")]
    public static void UnTar(this string file, string destPath)
    {
      using var stream = File.OpenRead(file);
      using var reader = ReaderFactory.OpenReader(stream);
      while (reader.MoveToNextEntry())
      {
        if (!reader.Entry.IsDirectory)
        {
          reader.WriteEntryToDirectory(destPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }
      }
    }
  }
}
