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
      var lines = output.Split(new[] { '\n' });
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
            currentBlock = new List<string>();
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
        var headers = SplitTopHeaderLine(headerLine);

        var processes = new ComposeProcesses
        {
          Service = containerName,
          ContainerId = containerName
        };

        for (var i = 2; i < block.Count; i++)
        {
          var row = ParseTopRow(block[i], headers);
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
    private static string[] SplitTopHeaderLine(string headerLine)
    {
      return headerLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Parses a single process data row, splitting by whitespace with the
    /// last column receiving all remaining text (to handle commands with spaces).
    /// </summary>
    private static Dictionary<string, string> ParseTopRow(
        string line, string[] headers)
    {
      var dict = new Dictionary<string, string>();
      if (headers.Length == 0)
        return dict;

      var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 0)
        return dict;

      // All columns except the last get one token each.
      // The last column gets everything remaining.
      var lastHeaderIndex = headers.Length - 1;

      for (var col = 0; col < headers.Length; col++)
      {
        if (col < lastHeaderIndex)
        {
          dict[headers[col]] = col < parts.Length ? parts[col] : string.Empty;
        }
        else
        {
          // Last column: join all remaining parts
          if (col < parts.Length)
          {
            dict[headers[col]] = string.Join(" ",
                parts.Skip(col));
          }
          else
          {
            dict[headers[col]] = string.Empty;
          }
        }
      }

      return dict;
    }
  }
}
