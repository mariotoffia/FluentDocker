using System;
using System.Text.RegularExpressions;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Components;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Parses human-readable CLI prune output from Docker and Podman.
  /// </summary>
  public static class CliPruneOutputParser
  {
    private static readonly Regex BareImageIdRegex =
        new Regex(@"^(?:sha256:)?[a-f0-9]{12,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SimpleNameRegex =
        new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses output from image prune.
    /// </summary>
    public static ImagePruneResult ParseImagePruneOutput(string output)
    {
      var result = new ImagePruneResult();

      try
      {
        if (string.IsNullOrWhiteSpace(output))
          return result;

        var sawDeletedImagesHeader = false;
        var inDeletedImagesSection = false;

        foreach (var rawLine in SplitLines(output))
        {
          var line = rawLine.Trim();
          if (string.IsNullOrEmpty(line))
            continue;

          if (IsHeader(line, "Deleted Images:"))
          {
            sawDeletedImagesHeader = true;
            inDeletedImagesSection = true;
            continue;
          }

          if (IsAnyDeletedSectionHeader(line))
          {
            inDeletedImagesSection = false;
            continue;
          }

          if (TryParseReclaimedSpaceLine(line, out var reclaimed))
          {
            result.SpaceReclaimed = reclaimed;
            continue;
          }

          if (IsIgnorablePruneLine(line))
            continue;

          if (sawDeletedImagesHeader && !inDeletedImagesSection)
            continue;

          if (TryParseImageDeletionLine(line, out var deleted))
            result.ImagesDeleted.Add(deleted);
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Image prune output parsing failed: {ex.Message}");
        return new ImagePruneResult();
      }

      return result;
    }

    /// <summary>
    /// Parses output from network prune.
    /// </summary>
    public static NetworkPruneResult ParseNetworkPruneOutput(string output)
    {
      var result = new NetworkPruneResult();

      try
      {
        if (string.IsNullOrWhiteSpace(output))
          return result;

        var sawDeletedNetworksHeader = false;
        var inDeletedNetworksSection = false;

        foreach (var rawLine in SplitLines(output))
        {
          var line = rawLine.Trim();
          if (string.IsNullOrEmpty(line))
            continue;

          if (IsHeader(line, "Deleted Networks:"))
          {
            sawDeletedNetworksHeader = true;
            inDeletedNetworksSection = true;
            continue;
          }

          if (IsAnyDeletedSectionHeader(line))
          {
            inDeletedNetworksSection = false;
            continue;
          }

          if (TryParseReclaimedSpaceLine(line, out _))
            continue;

          if (IsIgnorablePruneLine(line))
            continue;

          if (sawDeletedNetworksHeader && !inDeletedNetworksSection)
            continue;

          if (!LooksLikeSimpleName(line))
            continue;

          result.NetworksDeleted.Add(line);
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Network prune output parsing failed: {ex.Message}");
        return new NetworkPruneResult();
      }

      return result;
    }

    /// <summary>
    /// Parses output from volume prune.
    /// </summary>
    public static VolumePruneResult ParseVolumePruneOutput(string output)
    {
      var result = new VolumePruneResult();

      try
      {
        if (string.IsNullOrWhiteSpace(output))
          return result;

        var sawDeletedVolumesHeader = false;
        var inDeletedVolumesSection = false;

        foreach (var rawLine in SplitLines(output))
        {
          var line = rawLine.Trim();
          if (string.IsNullOrEmpty(line))
            continue;

          if (IsHeader(line, "Deleted Volumes:"))
          {
            sawDeletedVolumesHeader = true;
            inDeletedVolumesSection = true;
            continue;
          }

          if (IsAnyDeletedSectionHeader(line))
          {
            inDeletedVolumesSection = false;
            continue;
          }

          if (TryParseReclaimedSpaceLine(line, out var reclaimed))
          {
            result.SpaceReclaimed = reclaimed;
            continue;
          }

          if (IsIgnorablePruneLine(line))
            continue;

          if (sawDeletedVolumesHeader && !inDeletedVolumesSection)
            continue;

          if (!LooksLikeSimpleName(line))
            continue;

          result.VolumesDeleted.Add(line);
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"Volume prune output parsing failed: {ex.Message}");
        return new VolumePruneResult();
      }

      return result;
    }

    /// <summary>
    /// Parses output from system prune.
    /// </summary>
    public static SystemPruneResult ParseSystemPruneOutput(string output)
    {
      var result = new SystemPruneResult();

      try
      {
        if (string.IsNullOrWhiteSpace(output))
          return result;

        var section = PruneSection.None;

        foreach (var rawLine in SplitLines(output))
        {
          var line = rawLine.Trim();
          if (string.IsNullOrEmpty(line))
            continue;

          if (TryGetSection(line, out var parsedSection))
          {
            section = parsedSection;
            continue;
          }

          if (TryParseReclaimedSpaceLine(line, out var reclaimed))
          {
            result.SpaceReclaimed = reclaimed;
            continue;
          }

          if (IsIgnorablePruneLine(line))
            continue;

          switch (section)
          {
            case PruneSection.Containers:
              result.ContainersDeleted.Add(line);
              break;
            case PruneSection.Images:
              if (TryParseImageDeletionLine(line, out var deletedImage))
                result.ImagesDeleted.Add(deletedImage);
              break;
            case PruneSection.Networks:
              result.NetworksDeleted.Add(line);
              break;
            case PruneSection.Volumes:
              result.VolumesDeleted.Add(line);
              break;
            case PruneSection.BuildCache:
              result.BuildCacheDeleted.Add(line);
              break;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"System prune output parsing failed: {ex.Message}");
        return new SystemPruneResult();
      }

      return result;
    }

    private static string[] SplitLines(string output)
    {
      return output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static bool IsHeader(string line, string header)
    {
      return line.Equals(header, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnyDeletedSectionHeader(string line)
    {
      return line.StartsWith("Deleted ", StringComparison.OrdinalIgnoreCase)
             && line.EndsWith(':');
    }

    private static bool TryGetSection(string line, out PruneSection section)
    {
      if (IsHeader(line, "Deleted Containers:"))
      {
        section = PruneSection.Containers;
        return true;
      }

      if (IsHeader(line, "Deleted Images:"))
      {
        section = PruneSection.Images;
        return true;
      }

      if (IsHeader(line, "Deleted Networks:"))
      {
        section = PruneSection.Networks;
        return true;
      }

      if (IsHeader(line, "Deleted Volumes:"))
      {
        section = PruneSection.Volumes;
        return true;
      }

      if (IsHeader(line, "Deleted build cache objects:")
          || IsHeader(line, "Deleted build cache:")
          || IsHeader(line, "Deleted Build Cache:")
          || IsHeader(line, "Deleted Build Cache Objects:"))
      {
        section = PruneSection.BuildCache;
        return true;
      }

      if (IsAnyDeletedSectionHeader(line))
      {
        section = PruneSection.None;
        return true;
      }

      section = PruneSection.None;
      return false;
    }

    private static bool IsIgnorablePruneLine(string line)
    {
      return line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)
             || line.StartsWith("This will remove", StringComparison.OrdinalIgnoreCase)
             || line.StartsWith("Are you sure", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSimpleName(string line)
    {
      return SimpleNameRegex.IsMatch(line);
    }

    private static bool TryParseReclaimedSpaceLine(string line, out long bytes)
    {
      if (!line.StartsWith("Total reclaimed space:", StringComparison.OrdinalIgnoreCase))
      {
        bytes = 0;
        return false;
      }

      var colonIndex = line.IndexOf(':');
      if (colonIndex < 0 || colonIndex == line.Length - 1)
      {
        bytes = 0;
        return true;
      }

      var value = line.Substring(colonIndex + 1).Trim();
      bytes = DockerCliSystemDriver.ParseHumanReadableBytes(value);
      return true;
    }

    private static bool TryParseImageDeletionLine(string line, out string deleted)
    {
      if (line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase))
      {
        deleted = line.Substring("deleted:".Length).Trim();
        return !string.IsNullOrEmpty(deleted);
      }

      if (line.StartsWith("untagged:", StringComparison.OrdinalIgnoreCase))
      {
        deleted = line.Substring("untagged:".Length).Trim();
        return !string.IsNullOrEmpty(deleted);
      }

      if (BareImageIdRegex.IsMatch(line))
      {
        deleted = line;
        return true;
      }

      deleted = null;
      return false;
    }

    private enum PruneSection
    {
      None,
      Containers,
      Images,
      Networks,
      Volumes,
      BuildCache
    }
  }
}
