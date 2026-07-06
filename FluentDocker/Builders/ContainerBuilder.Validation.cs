using System;
using System.Text.RegularExpressions;
using FluentDocker.Common;

namespace FluentDocker.Builders
{
  internal sealed partial class ContainerBuilder
  {
    private static readonly Regex ContainerNameRegex = new(
        "^[a-zA-Z0-9][a-zA-Z0-9_.-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private void ValidateHardenedConfiguration()
    {
      ValidateContainerName();
      ValidateEnvironmentKeys();
      ValidateVolumeSources();
    }

    private void ValidateContainerName()
    {
      if (string.IsNullOrEmpty(_name))
        return;
      if (!ContainerNameRegex.IsMatch(_name))
        throw new FluentDockerException(
            $"Invalid container name '{_name}'. Names must match ^[a-zA-Z0-9][a-zA-Z0-9_.-]+$.");
    }

    private void ValidateEnvironmentKeys()
    {
      foreach (var key in _environment.Keys)
      {
        if (key.Contains('='))
          throw new FluentDockerException(
              $"Environment key '{key}' must not contain '='. Pass the key and value separately.");
      }
    }

    private void ValidateVolumeSources()
    {
      foreach (var volume in _volumes)
      {
        var source = GetVolumeSource(volume);
        if (IsRelativeBindMountSource(source))
          throw new FluentDockerException(
              $"Relative bind mount source '{source}' is ambiguous. Use an absolute path or a named volume.");
      }
    }

    private static bool IsRelativeBindMountSource(string source)
    {
      if (string.IsNullOrWhiteSpace(source))
        return false;
      // A Windows drive-absolute (C:\, C:/) or UNC (\\server\share) path is absolute even when the
      // client OS is Unix, where Path.IsPathRooted does not recognise it. Docker forwards such
      // sources to a Windows daemon verbatim, so they are not ambiguous relative paths.
      if (System.IO.Path.IsPathRooted(source) || IsWindowsAbsoluteSource(source))
        return false;
      return source.StartsWith('.') ||
          source.Contains('/') ||
          source.Contains('\\');
    }

    private static bool IsWindowsAbsoluteSource(string source) =>
        source.StartsWith("\\\\", StringComparison.Ordinal) ||
        (source.Length >= 3 &&
            char.IsAsciiLetter(source[0]) &&
            source[1] == ':' &&
            (source[2] == '\\' || source[2] == '/'));
  }
}
