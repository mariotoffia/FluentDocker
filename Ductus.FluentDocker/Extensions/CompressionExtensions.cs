using System.IO;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Ductus.FluentDocker.Extensions
{
  public static class CompressionExtensions
  {
    public static void UnTar(this string file, string destPath)
    {
      using (var stream = File.OpenRead(file))
      {
        using (var reader = ReaderFactory.Open(stream))
        {
          while (reader.MoveToNextEntry())
          {
            if (!reader.Entry.IsDirectory)
            {
              reader.WriteEntryToDirectory(destPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true});
            }
          }
        }
      }
    }
  }
}
