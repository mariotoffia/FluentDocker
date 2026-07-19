using System;
using System.IO;

namespace FluentDocker.Tests.Utilities
{
  /// <summary>
  /// Creates a directory containing fake (empty, executable) container-CLI binaries so unit
  /// tests can initialize real driver packs hermetically — without docker or podman installed
  /// on the machine. Pack initialization only resolves (stats) binaries; it never executes
  /// them, so an empty file with the execute bit is sufficient.
  /// </summary>
  public static class FakeBinaryDirectory
  {
    /// <summary>
    /// Creates a unique directory under the test output root containing fake executables with
    /// the given names (for example <c>"docker"</c>, <c>"podman"</c>). On Windows the files
    /// are created with an <c>.exe</c> extension. The directory lives in the git-ignored test
    /// output tree; callers may delete it but leaking it is harmless.
    /// </summary>
    /// <param name="binaryNames">The binary base names to create.</param>
    /// <returns>The directory path, suitable for <c>DriverContext.SearchPaths</c>.</returns>
    public static string Create(params string[] binaryNames)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", $"fake-bins-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);
      foreach (var name in binaryNames)
      {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var path = Path.Combine(dir, file);
        File.WriteAllText(path, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
          File.SetUnixFileMode(
              path,
              UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
      }

      return dir;
    }
  }
}
