#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Model.Models;

namespace FluentDocker.Drivers.Docker.Cli.Components.Parsing
{
  /// <summary>
  /// Table-output fallbacks for <see cref="ModelJsonParser"/>: parses the plain-text tables
  /// printed by <c>docker model ls</c>/<c>ps</c>/<c>df</c> for DMR versions/subcommands that
  /// do not support <c>--json</c>. Split from the main file to keep partial files under the
  /// line-count limit.
  /// </summary>
  public static partial class ModelJsonParser
  {
    /// <summary>
    /// Parses the <c>docker model ls</c> table, distinguishing a genuinely empty table from
    /// an unrecognizable payload. Columns are located by header offsets (not whitespace
    /// splitting) so a blank cell does not shift subsequent column indices.
    /// </summary>
    /// <param name="text">The raw <c>ls</c> table output.</param>
    /// <param name="models">The parsed models (empty on failure).</param>
    /// <returns>
    /// <c>true</c> when a <c>MODEL NAME</c> header with at least 5 columns was found (including
    /// on blank/whitespace input); <c>false</c> when the header is missing or too narrow.
    /// </returns>
    public static bool TryParseLsTable(string text, out IList<ModelInfo> models)
    {
      var result = new List<ModelInfo>();
      models = result;
      if (!TryDataRows(text, "MODEL NAME", 5, out var rows))
        return false;

      foreach (var fields in rows)
      {
        if (!ModelReference.TryParse(fields[0], out var reference))
          continue;

        var id = fields.FirstOrDefault(f => HexId.IsMatch(f));
        var sizeField = fields.LastOrDefault(f => SizeRegex.IsMatch(f.Trim()));
        result.Add(new ModelInfo
        {
          Reference = reference,
          Id = id,
          ParameterCount = fields.ElementAtOrDefault(1),
          Quantization = fields.ElementAtOrDefault(2),
          Architecture = fields.ElementAtOrDefault(3),
          Size = sizeField != null ? ParseSize(sizeField) : 0,
          Tags = [fields[0]]
        });
      }

      return true;
    }

    /// <summary>
    /// Parses the <c>docker model ps</c> table (no <c>--json</c> in current DMR), distinguishing
    /// a genuinely empty table from an unrecognizable payload.
    /// </summary>
    /// <param name="text">The raw <c>ps</c> table output.</param>
    /// <param name="running">The parsed running models (empty on failure).</param>
    /// <returns>
    /// <c>true</c> when a <c>MODEL NAME</c>/<c>BACKEND</c>/<c>MODE</c> header was found
    /// (including on blank/whitespace input); <c>false</c> when the header does not match.
    /// </returns>
    public static bool TryParsePsTable(string text, out IList<RunningModel> running)
    {
      var result = new List<RunningModel>();
      running = result;
      if (!TryDataRows(text, "MODEL NAME", 3, out var rows, "MODEL NAME", "BACKEND", "MODE"))
        return false;

      foreach (var fields in rows)
      {
        if (!ModelReference.TryParse(fields[0], out var reference))
          continue;

        result.Add(new RunningModel
        {
          Reference = reference,
          Backend = fields.ElementAtOrDefault(1),
          Mode = fields.ElementAtOrDefault(2)
        });
      }

      return true;
    }

    /// <summary>
    /// Parses the <c>docker model df</c> table, distinguishing a genuinely empty table from
    /// an unrecognizable payload. Only the <c>Models</c> row's reclaimable size is extracted.
    /// </summary>
    /// <param name="text">The raw <c>df</c> table output.</param>
    /// <param name="usage">The parsed disk usage (zeroed on failure).</param>
    /// <returns>
    /// <c>true</c> when a <c>TYPE</c> header with at least 2 columns was found (including on
    /// blank/whitespace input); <c>false</c> when the header is missing or too narrow.
    /// </returns>
    public static bool TryParseDfTable(string text, out ModelDiskUsage usage)
    {
      long modelsBytes = 0;
      usage = new ModelDiskUsage();
      if (!TryDataRows(text, "TYPE", 2, out var rows))
        return false;

      foreach (var fields in rows)
      {
        if (fields.Length >= 2 && fields[0].StartsWith("Models", StringComparison.OrdinalIgnoreCase))
          modelsBytes = ParseSize(fields[^1]);
      }

      usage = new ModelDiskUsage { ModelsSizeBytes = modelsBytes };
      return true;
    }

    // Scans `text` for a header line containing `headerToken`, validates its column count
    // (and optional leading column names via `expectedPrefix`), then extracts every subsequent
    // non-blank line's fields at the header's column offsets. Returns false only when a
    // non-blank payload lacks a recognizable header — blank/whitespace input returns true
    // with zero rows, matching the exception-safe "empty vs malformed" contract used throughout
    // this parser.
    private static bool TryDataRows(string text, string headerToken, int minColumns,
        out IList<string[]> rows, params string[] expectedPrefix)
    {
      rows = [];
      var lines = (text ?? string.Empty).Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
      string headerLine = null;
      foreach (var raw in lines)
      {
        var line = raw.TrimEnd();
        if (line.Length == 0)
          continue;

        if (headerLine == null)
        {
          if (line.Contains(headerToken, StringComparison.OrdinalIgnoreCase))
            headerLine = line;
          continue;
        }

        var headerFields = ParseRowByHeaderOffsets(headerLine, headerLine);
        if (headerFields.Length < minColumns ||
            (expectedPrefix.Length > 0 && (headerFields.Length < expectedPrefix.Length ||
             expectedPrefix.Where((expected, i) => !string.Equals(headerFields[i], expected, StringComparison.OrdinalIgnoreCase)).Any())))
          return false;

        // Use header-column offsets to extract fields so a blank value in one column
        // does not shift subsequent column indices (avoids split-by-whitespace ambiguity).
        var fields = ParseRowByHeaderOffsets(headerLine, line);
        if (fields.Length > 0 && fields[0].Length > 0)
          rows.Add(fields);
      }

      return headerLine != null || string.IsNullOrWhiteSpace(text);
    }

    // Splits `dataLine` at the column start positions detected in `headerLine` (a new column
    // begins after 2+ consecutive spaces), so ragged/blank cells do not shift later columns.
    private static string[] ParseRowByHeaderOffsets(string headerLine, string dataLine)
    {
      // Detect column start positions from the header (each new column begins after 2+ spaces).
      var cols = new List<int> { 0 }; // first column always starts at 0
      var i = 0;
      while (i < headerLine.Length)
      {
        // A column boundary is where two-or-more consecutive spaces end (next non-space).
        if (headerLine[i] == ' ' && i + 1 < headerLine.Length && headerLine[i + 1] == ' ')
        {
          // skip all spaces
          while (i < headerLine.Length && headerLine[i] == ' ')
            i++;
          if (i < headerLine.Length)
            cols.Add(i);
        }
        else
        {
          i++;
        }
      }

      var values = new string[cols.Count];
      for (var c = 0; c < cols.Count; c++)
      {
        var colStart = cols[c];
        var colEnd = c + 1 < cols.Count ? cols[c + 1] : dataLine.Length;
        if (colStart >= dataLine.Length)
        {
          values[c] = string.Empty;
          continue;
        }
        var end = Math.Min(colEnd, dataLine.Length);
        values[c] = dataLine[colStart..end].Trim();
      }
      return values;
    }
  }
}
