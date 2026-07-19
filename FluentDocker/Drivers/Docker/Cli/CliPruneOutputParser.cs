using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using FluentDocker.Common;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli
{
  /// <summary>
  /// Parses human-readable CLI prune output from Docker and Podman.
  /// </summary>
  /// <remarks>Best-effort parser: all parse methods catch malformed output, log Debug, and return partial results.</remarks>
  public static partial class CliPruneOutputParser
  {
    private static readonly Regex BareImageIdRegex =
        BareImageIdPattern();
    private static readonly Regex SimpleNameRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Parses output from image prune.
    /// </summary>
    public static ImagePruneResult ParseImagePruneOutput(string output, ILogger? logger = null)
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
        logger?.LogDebug(ex, "Image prune output parsing failed");
        return result;
      }

      return result;
    }

    /// <summary>
    /// Parses output from network prune.
    /// </summary>
    public static NetworkPruneResult ParseNetworkPruneOutput(string output, ILogger? logger = null)
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
        logger?.LogDebug(ex, "Network prune output parsing failed");
        return result;
      }

      return result;
    }

    /// <summary>
    /// Parses output from volume prune.
    /// </summary>
    public static VolumePruneResult ParseVolumePruneOutput(string output, ILogger? logger = null)
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
        logger?.LogDebug(ex, "Volume prune output parsing failed");
        return result;
      }

      return result;
    }

    /// <summary>
    /// Parses output from system prune.
    /// </summary>
    public static SystemPruneResult ParseSystemPruneOutput(string output, ILogger? logger = null)
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

          // Same shape validation as the dedicated network/volume parsers: a stray
          // warning/diagnostic line inside a section must not be misreported as a deleted id.
          switch (section)
          {
            case PruneSection.Containers:
              if (LooksLikeSimpleName(line))
                result.ContainersDeleted.Add(line);
              break;
            case PruneSection.Images:
              if (TryParseImageDeletionLine(line, out var deletedImage))
                result.ImagesDeleted.Add(deletedImage);
              break;
            case PruneSection.Networks:
              if (LooksLikeSimpleName(line))
                result.NetworksDeleted.Add(line);
              break;
            case PruneSection.Volumes:
              if (LooksLikeSimpleName(line))
                result.VolumesDeleted.Add(line);
              break;
            case PruneSection.BuildCache:
              if (LooksLikeSimpleName(line))
                result.BuildCacheDeleted.Add(line);
              break;
          }
        }
      }
      catch (Exception ex)
      {
        logger?.LogDebug(ex, "System prune output parsing failed");
        return result;
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

      var value = line[(colonIndex + 1)..].Trim();
      bytes = CliByteParser.ParseHumanReadableBytes(value);
      return true;
    }

    private static bool TryParseImageDeletionLine(string line, [MaybeNullWhen(false)] out string deleted)
    {
      if (line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase))
      {
        deleted = line["deleted:".Length..].Trim();
        return !string.IsNullOrEmpty(deleted);
      }

      if (line.StartsWith("untagged:", StringComparison.OrdinalIgnoreCase))
      {
        deleted = line["untagged:".Length..].Trim();
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

    [GeneratedRegex(@"^(?:sha256:)?[a-f0-9]{12,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex BareImageIdPattern();
  }
}
