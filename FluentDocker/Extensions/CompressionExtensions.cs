using System.IO;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace FluentDocker.Extensions
{
  public static class CompressionExtensions
  {
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
