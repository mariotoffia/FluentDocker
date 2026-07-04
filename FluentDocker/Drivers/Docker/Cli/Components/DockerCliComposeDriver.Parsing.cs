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
    /// Parses the text table output of <c>docker compose top</c> into a list
    /// of <see cref="ComposeProcesses"/>. The output consists of blocks
    /// separated by blank lines, where the first line is the container name,
    /// the second line contains column headers, and subsequent lines are
    /// process data rows.
    /// </summary>
    /// <param name="output">Raw CLI output from <c>docker compose top</c>.</param>
    /// <returns>Parsed list of processes grouped by container.</returns>
    public static IList<ComposeProcesses> ParseTopOutput(string output)
    {
      var result = new List<ComposeProcesses>();

      if (string.IsNullOrWhiteSpace(output))
        return result;

      // Split into blocks separated by one or more blank lines.
      var lines = output.Split(NewlineSeparator);
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

        var processes = new ComposeProcesses
        {
          Service = containerName,
          ContainerId = containerName
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
