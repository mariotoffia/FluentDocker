#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  ///   Helper to proper delete a directory and subdirectories.
  /// </summary>
  /// <remarks>
  ///   This class is taken from LibGit2Sharp.Tests.TestHelpers.
  /// </remarks>
  public static class DirectoryHelper
  {
    static DirectoryHelper() => GetTempPath = Path.GetTempPath;
    private static Func<string> _getTempPath = null!;

    private static readonly Dictionary<string, string> ToRename = new()
    {
      {"dot_git", ".git"},
      {"gitmodules", ".gitmodules"}
    };

    private static readonly Type[] Whitelist = [typeof(IOException), typeof(UnauthorizedAccessException)];

    /// <summary>
    /// Gets a delegate that returns a writeable temporary folder path.
    /// </summary>
    /// <remarks>
    ///  This mutable process-wide hook is intended for test hosts that must redirect temporary
    ///  files. Set it at startup only; changing it while other operations run affects all callers.
    ///  The default uses the <see cref="Path.GetTempPath"/> implementation.
    /// </remarks>
    public static Func<string> GetTempPath
    {
      get => Volatile.Read(ref _getTempPath);
      set => Interlocked.Exchange(ref _getTempPath, value ?? Path.GetTempPath);
    }

    /// <summary>Recursively copies all files and subdirectories from source to target.</summary>
    /// <remarks>
    /// During copy, LibGit2Sharp-style fixture names are restored:
    /// <c>dot_git</c> becomes <c>.git</c>, and <c>gitmodules</c> becomes <c>.gitmodules</c>.
    /// </remarks>
    /// <param name="source">The source directory to copy from.</param>
    /// <param name="target">The target directory to copy into.</param>
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
      return ToRename.TryGetValue(name, out var renamed) ? renamed : name;
    }

    /// <summary>Deletes a directory and all its contents, retrying on transient IO errors.</summary>
    /// <param name="directoryPath">The path of the directory to delete.</param>
    public static void DeleteDirectory(string directoryPath)
    {
      DeleteDirectory(directoryPath, true);
    }

    /// <summary>Deletes a directory and all its contents, optionally suppressing final retry failures.</summary>
    /// <param name="directoryPath">The path of the directory to delete.</param>
    /// <param name="throwOnFailure">Whether to throw the final retry exception when deletion fails.</param>
    public static void DeleteDirectory(string directoryPath, bool throwOnFailure)
    {
      if (!Directory.Exists(directoryPath))
        return;

      DeleteDirectory(directoryPath, 5, 16, 2, throwOnFailure);
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

    private static void DeleteDirectory(
        string directoryPath, int maxAttempts, int initialTimeout, int timeoutFactor, bool throwOnFailure)
    {
      for (var attempt = 1; attempt <= maxAttempts; attempt++)
        try
        {
          NormalizeAttributes(directoryPath);
          Directory.Delete(directoryPath, true);
          return;
        }
        catch (DirectoryNotFoundException)
        {
          return;
        }
        catch (Exception ex)
        {
          var caughtExceptionType = ex.GetType();

          if (!Whitelist.Any(knownExceptionType => knownExceptionType.IsAssignableFrom(caughtExceptionType)))
            throw;

          if (attempt >= maxAttempts)
          {
            if (throwOnFailure)
              throw;
            return;
          }

          Thread.Sleep(initialTimeout * (int)Math.Pow(timeoutFactor, attempt - 1));
        }
    }
  }
}
