using System;
using System.Collections.Generic;
using System.Linq;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI compose driver: parsing methods for text-based CLI output.
  /// </summary>
  public partial class DockerCliComposeDriver
  {
    /// <summary>
    /// Parses the text table output of <c>docker compose top</c> into a list of
    /// <see cref="ComposeProcesses"/>, auto-detecting the CLI output shape.
    /// <para>
    /// Modern Compose (≥ v2.24, live-verified on v5.1.4) emits a <b>single table</b>
    /// whose first column is <c>SERVICE</c> (header:
    /// <c>SERVICE # UID PID PPID C STIME TTY TIME CMD</c>) with one row per process;
    /// rows are grouped by the <c>SERVICE</c> column. Pre-2.24 Compose emitted
    /// per-container <b>blocks</b> separated by blank lines (line 1 = container name,
    /// line 2 = column headers, remaining lines = process rows).
    /// </para>
    /// The single-table format is selected when the first non-blank line's first token
    /// is <c>SERVICE</c>; otherwise the legacy block parser is used as a fallback.
    /// </summary>
    /// <param name="output">Raw CLI output from <c>docker compose top</c>.</param>
    /// <param name="containersByName">Optional compose ps records keyed by container name.</param>
    /// <returns>Parsed list of processes grouped by container (legacy) or service (modern).</returns>
    public static IList<ComposeProcesses> ParseTopOutput(
        string output,
        IReadOnlyDictionary<string, ComposeServiceInfo> containersByName = null)
    {
      if (string.IsNullOrWhiteSpace(output))
        return new List<ComposeProcesses>();

      var lines = output.Split(NewlineSeparator);

      return IsSingleTableTop(lines)
          ? ParseSingleTableTop(lines, containersByName)
          : ParseLegacyBlockTop(lines, containersByName);
    }

    /// <summary>
    /// Detects the modern single-table <c>docker compose top</c> format: the first
    /// non-blank line's first whitespace-delimited token is <c>SERVICE</c>.
    /// </summary>
    private static bool IsSingleTableTop(string[] lines)
    {
      foreach (var rawLine in lines)
      {
        var line = rawLine.TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(line))
          continue;

        var firstToken = line.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries);
        return firstToken.Length > 0 &&
               string.Equals(firstToken[0], "SERVICE", StringComparison.OrdinalIgnoreCase);
      }

      return false;
    }

    /// <summary>
    /// Parses the modern single-table format (header row + one process row per line),
    /// grouping rows by the <c>SERVICE</c> column. The container id/name are joined from
    /// the <c>compose ps</c> lookup when exactly one container matches the service
    /// (ambiguous when scaled — left <c>null</c> rather than guessing).
    /// </summary>
    private static IList<ComposeProcesses> ParseSingleTableTop(
        string[] lines,
        IReadOnlyDictionary<string, ComposeServiceInfo> containersByName)
    {
      var result = new List<ComposeProcesses>();
      var byService = new Dictionary<string, ComposeProcesses>(StringComparer.Ordinal);
      TopColumn[] columns = null;
      string serviceColumn = null;

      foreach (var rawLine in lines)
      {
        var line = rawLine.TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(line))
          continue;

        if (columns == null)
        {
          columns = SplitTopHeaderLine(line);
          serviceColumn = columns.FirstOrDefault(c =>
              string.Equals(c.Name, "SERVICE", StringComparison.OrdinalIgnoreCase)).Name;
          continue;
        }

        var row = ParseTopRow(line, columns);
        if (row.Count == 0 || serviceColumn == null ||
            !row.TryGetValue(serviceColumn, out var service) || string.IsNullOrEmpty(service))
          continue;

        row.Remove(serviceColumn); // SERVICE is the grouping key, not a process attribute.

        if (!byService.TryGetValue(service, out var processes))
        {
          processes = new ComposeProcesses { Service = service };
          ResolveContainerForService(service, containersByName, processes);
          byService[service] = processes;
          result.Add(processes);
        }

        processes.Processes.Add(row);
      }

      return result;
    }

    /// <summary>
    /// Populates <see cref="ComposeProcesses.ContainerId"/>/<see cref="ComposeProcesses.ContainerName"/>
    /// from the <c>compose ps</c> lookup only when exactly one container maps to the service; a scaled
    /// service (multiple containers) or an absent join leaves both <c>null</c> (honest best-effort).
    /// </summary>
    private static void ResolveContainerForService(
        string service,
        IReadOnlyDictionary<string, ComposeServiceInfo> containersByName,
        ComposeProcesses processes)
    {
      if (containersByName == null)
        return;

      ComposeServiceInfo match = null;
      var count = 0;
      foreach (var info in containersByName.Values)
      {
        if (!string.Equals(info.Name, service, StringComparison.Ordinal))
          continue;

        match = info;
        if (++count > 1)
          return; // ambiguous (scaled) -> leave null
      }

      if (count == 1)
      {
        processes.ContainerId = match.ContainerId;
        processes.ContainerName = match.ContainerName;
      }
    }

    /// <summary>
    /// Parses the legacy (pre-2.24) per-container block format: blocks separated by blank
    /// lines, where the first line is the container name, the second line contains column
    /// headers, and subsequent lines are process data rows.
    /// </summary>
    private static IList<ComposeProcesses> ParseLegacyBlockTop(
        string[] lines,
        IReadOnlyDictionary<string, ComposeServiceInfo> containersByName)
    {
      var result = new List<ComposeProcesses>();

      // Split into blocks separated by one or more blank lines.
      var blocks = new List<List<string>>();
      var currentBlock = new List<string>();

      foreach (var rawLine in lines)
      {
        var line = rawLine.TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(line))
        {
          if (currentBlock.Count > 0)
          {
            blocks.Add(currentBlock);
            currentBlock = [];
          }
        }
        else
        {
          currentBlock.Add(line);
        }
      }

      if (currentBlock.Count > 0)
        blocks.Add(currentBlock);

      foreach (var block in blocks)
      {
        if (block.Count < 2)
          continue; // Need at least container name + header row

        var containerName = block[0].Trim();
        var headerLine = block[1];
        var columns = SplitTopHeaderLine(headerLine);
        // ponytail: compose top only names the container; without ps JSON this is the best-effort fallback.
        var service = containerName;
        string containerId = null;
        if (containersByName?.TryGetValue(containerName, out var serviceInfo) == true)
        {
          service = serviceInfo.Name;
          containerId = serviceInfo.ContainerId;
        }

        var processes = new ComposeProcesses
        {
          Service = service,
          ContainerId = containerId,
          ContainerName = containerName
        };

        for (var i = 2; i < block.Count; i++)
        {
          var row = ParseTopRow(block[i], columns);
          if (row.Count > 0)
            processes.Processes.Add(row);
        }

        result.Add(processes);
      }

      return result;
    }

    /// <summary>
    /// Splits a header line into column names by whitespace.
    /// </summary>
    private static TopColumn[] SplitTopHeaderLine(string headerLine)
    {
      var headers = headerLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
      var columns = new TopColumn[headers.Length];
      var searchStart = 0;
      for (var i = 0; i < headers.Length; i++)
      {
        var start = headerLine.IndexOf(headers[i], searchStart, StringComparison.Ordinal);
        columns[i] = new TopColumn(headers[i], start < 0 ? searchStart : start);
        searchStart = columns[i].Start + headers[i].Length;
      }

      return columns;
    }

    /// <summary>
    /// Parses a single process data row, splitting by whitespace with the
    /// last column receiving all remaining text (to handle commands with spaces).
    /// </summary>
    private static Dictionary<string, string> ParseTopRow(
        string line, TopColumn[] columns)
    {
      var dict = new Dictionary<string, string>();
      if (columns.Length == 0)
        return dict;

      for (var col = 0; col < columns.Length; col++)
      {
        var start = Math.Min(columns[col].Start, line.Length);
        var end = col + 1 < columns.Length ? Math.Min(columns[col + 1].Start, line.Length) : line.Length;
        dict[columns[col].Name] = line[start..end].Trim();
      }

      return dict;
    }

    private readonly struct TopColumn(string name, int start)
    {
      public string Name { get; } = name;
      public int Start { get; } = start;
    }
  }
}
