using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Ductus.FluentDocker.Common
{
  /// <summary>
  ///   Helper to proper delete a directory and subdirectories.
  /// </summary>
  /// <remarks>
  ///   This class is taken from the <see cref="LibGit2Sharp.Tests.TestHelpers" />.
  /// </remarks>
  public static class DirectoryHelper
  {
    private static readonly Dictionary<string, string> ToRename = new Dictionary<string, string>
    {
      {"dot_git", ".git"},
      {"gitmodules", ".gitmodules"}
    };

    private static readonly Type[] Whitelist = { typeof(IOException), typeof(UnauthorizedAccessException) };

    public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
      // From http://stackoverflow.com/questions/58744/best-way-to-copy-the-entire-contents-of-a-directory-in-c/58779#58779

      foreach (var dir in source.GetDirectories())
        CopyFilesRecursively(dir, target.CreateSubdirectory(Rename(dir.Name)));
      foreach (var file in source.GetFiles())
        file.CopyTo(Path.Combine(target.FullName, Rename(file.Name)));
    }

    private static string Rename(string name)
    {
      return ToRename.ContainsKey(name) ? ToRename[name] : name;
    }

    public static void DeleteDirectory(string directoryPath)
    {
      if (!Directory.Exists(directoryPath))
        return;

      NormalizeAttributes(directoryPath);
      DeleteDirectory(directoryPath, 5, 16, 2);
    }

    private static void NormalizeAttributes(string directoryPath)
    {
      var filePaths = Directory.GetFiles(directoryPath);
      var subdirectoryPaths = Directory.GetDirectories(directoryPath);

      foreach (var filePath in filePaths)
        File.SetAttributes(filePath, FileAttributes.Normal);
      foreach (var subdirectoryPath in subdirectoryPaths)
        NormalizeAttributes(subdirectoryPath);
      File.SetAttributes(directoryPath, FileAttributes.Normal);
    }

    private static void DeleteDirectory(string directoryPath, int maxAttempts, int initialTimeout, int timeoutFactor)
    {
      for (var attempt = 1; attempt <= maxAttempts; attempt++)
        try
        {
          Directory.Delete(directoryPath, true);
          return;
        }
        catch (Exception ex)
        {
          var caughtExceptionType = ex.GetType();

          if (!Whitelist.Any(knownExceptionType => knownExceptionType.IsAssignableFrom(caughtExceptionType)))
            throw;

          if (attempt >= maxAttempts)
            continue;
          Thread.Sleep(initialTimeout * (int)Math.Pow(timeoutFactor, attempt - 1));
        }
    }
  }
}
