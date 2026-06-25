using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentDocker.Common;
using FluentDocker.Model.Models;

namespace FluentDocker.Drivers.Docker.Cli.Components.Parsing
{
  /// <summary>
  /// Maps Docker Model Runner CLI output (<c>--json</c> where available, table
  /// fallbacks otherwise) onto the model POCOs. All methods are exception-safe:
  /// malformed input yields an empty/null result rather than throwing.
  /// </summary>
  public static class ModelJsonParser
  {
    /// <summary>
    /// Upper bound (8 MiB, in chars — an over-estimate of UTF-8 bytes since every char is at
    /// least one byte) on a JSON payload this parser will parse. Real DMR <c>--json</c>
    /// output (model lists, inspect objects, NDJSON progress lines) is tiny; this only guards
    /// against pathological/hostile CLI output being fully parsed — which clones
    /// <see cref="JsonElement"/>s and materializes collections — without limit. Oversized
    /// input is rejected up front and yields the same safe empty/failure result as malformed
    /// input (the parser's exception-safe contract is preserved — it never throws).
    /// </summary>
    private const int MaxJsonInputChars = 8 * 1024 * 1024;

    private static readonly char[] LineSeparators = ['\n', '\r'];
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"^([\d.]+)\s*([KMGTP]?)(i?)B$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SizeTokenRegex = new(@"[\d.]+\s*[KMGTP]?i?B", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HexId = new(@"^[0-9a-f]{12,}$", RegexOptions.Compiled);
    private static readonly Regex PullRegex = new(@"([\d.]+\s*[KMGTP]?i?B)\s+of\s+([\d.]+\s*[KMGTP]?i?B)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses a <c>docker model ls --json</c> array (exception-safe; <c>[]</c> on malformed).</summary>
    public static IList<ModelInfo> ParseList(string json)
    {
      TryParseList(json, out var models);
      return models;
    }

    /// <summary>
    /// Parses a <c>docker model ls --json</c> array, distinguishing a genuinely empty
    /// result from a parse failure. Empty/whitespace input is a successful empty list;
    /// non-empty input that is not a JSON array (malformed, or an error object) returns
    /// <c>false</c> so callers do not silently report "zero models".
    /// </summary>
    /// <param name="json">The raw <c>ls --json</c> output.</param>
    /// <param name="models">The parsed models (empty on failure).</param>
    /// <returns><c>true</c> when parsed (possibly empty); <c>false</c> on a malformed payload.</returns>
    public static bool TryParseList(string json, out IList<ModelInfo> models)
    {
      models = [];
      if (string.IsNullOrWhiteSpace(json))
        return true;

      // Reject pathological output before parsing/cloning it — treat as a parse failure so
      // callers do not silently report "zero models" for an oversized payload.
      if (json.Length > MaxJsonInputChars)
        return false;

      try
      {
        var root = JsonHelper.ParseElement(json);
        if (root.ValueKind != JsonValueKind.Array)
          return false;

        models = [.. root.EnumerateArray().Select(MapModel)];
        return true;
      }
      catch (JsonException)
      {
        return false;
      }
    }

    /// <summary>Parses a single <c>docker model inspect</c> object.</summary>
    public static ModelInfo ParseInfo(string json)
    {
      // Bound the input before parsing/cloning — oversized payloads yield null, like malformed.
      if (json is { Length: > MaxJsonInputChars })
        return null;

      try
      {
        var el = JsonHelper.ParseElement(json);
        return el.ValueKind != JsonValueKind.Object ? null : MapModel(el);
      }
      catch (JsonException)
      {
        return null;
      }
    }

    /// <summary>Parses the <c>docker model ls</c> table (fallback when <c>--json</c> is unavailable).</summary>
    public static IList<ModelInfo> ParseLsTable(string text)
    {
      var result = new List<ModelInfo>();
      foreach (var fields in DataRows(text, "MODEL NAME"))
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

      return result;
    }

    /// <summary>Parses the <c>docker model ps</c> table (no <c>--json</c> in current DMR).</summary>
    public static IList<RunningModel> ParsePsTable(string text)
    {
      var result = new List<RunningModel>();
      foreach (var fields in DataRows(text, "MODEL NAME"))
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

      return result;
    }

    /// <summary>Parses the <c>docker model df</c> table.</summary>
    public static ModelDiskUsage ParseDfTable(string text)
    {
      long modelsBytes = 0;
      foreach (var fields in DataRows(text, "TYPE"))
      {
        if (fields.Length >= 2 && fields[0].StartsWith("Models", StringComparison.OrdinalIgnoreCase))
          modelsBytes = ParseSize(fields[^1]);
      }

      return new ModelDiskUsage { ModelsSizeBytes = modelsBytes };
    }

    /// <summary>
    /// Best-effort parse of <c>docker model prune</c> output. The exact format is not
    /// guaranteed across DMR versions, so the raw output is always preserved verbatim;
    /// removed-model lines and a reclaimed size are extracted only when recognizable.
    /// </summary>
    public static ModelPruneResult ParsePruneResult(string output)
    {
      output ??= string.Empty;
      var removed = new List<string>();
      long reclaimed = 0;

      foreach (var raw in output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
      {
        var line = raw.Trim();
        if (line.Length == 0)
          continue;

        if (line.Contains("reclaim", StringComparison.OrdinalIgnoreCase))
        {
          var match = SizeTokenRegex.Match(line);
          if (match.Success)
            reclaimed = ParseSize(match.Value);
          continue;
        }

        if (line.StartsWith("deleted", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("untagged", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("removed", StringComparison.OrdinalIgnoreCase))
          removed.Add(line);
      }

      return new ModelPruneResult { Removed = removed, ReclaimedBytes = reclaimed, RawOutput = output.Trim() };
    }

    /// <summary>Parses <c>docker model version</c> output.</summary>
    public static ModelRunnerVersion ParseVersion(string text)
    {
      string cli = null;
      string api = null;
      string engine = null;

      foreach (var raw in (text ?? string.Empty).Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
      {
        var line = raw.Trim();
        if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
        {
          var value = line["Version:".Length..].Trim();
          if (cli == null)
            cli = value;
          else
            api ??= value;
        }
        else if (line.StartsWith("Engine:", StringComparison.OrdinalIgnoreCase))
        {
          engine = line["Engine:".Length..].Trim();
        }
      }

      return new ModelRunnerVersion { CliVersion = cli, ApiVersion = api, EngineVersion = engine };
    }

    /// <summary>Parses a single line of <c>docker model pull</c> stdout into a progress event.</summary>
    public static ModelPullProgress ParsePullLine(string line)
    {
      if (string.IsNullOrWhiteSpace(line))
        return null;

      var trimmed = line.Trim();
      var match = PullRegex.Match(trimmed);
      if (match.Success)
      {
        return new ModelPullProgress
        {
          Status = "Downloading",
          Current = ParseSize(match.Groups[1].Value),
          Total = ParseSize(match.Groups[2].Value)
        };
      }

      return new ModelPullProgress { Status = trimmed };
    }

    /// <summary>
    /// Parses a single NDJSON line from the native <c>POST /models/create</c>
    /// progress stream (shape: <c>{"type":…,"message":…,"total":…,"layer":{"size":…,"current":…}}</c>).
    /// </summary>
    public static ModelPullProgress ParseNativePullProgress(string jsonLine)
    {
      if (string.IsNullOrWhiteSpace(jsonLine))
        return null;

      // A single NDJSON progress line is tiny; an oversized one is pathological — reject it.
      if (jsonLine.Length > MaxJsonInputChars)
        return null;

      try
      {
        var el = JsonHelper.ParseElement(jsonLine);
        if (el.ValueKind != JsonValueKind.Object)
          return null;

        var total = el.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0L;
        var current = 0L;
        if (el.TryGetProperty("layer", out var layer) && layer.ValueKind == JsonValueKind.Object)
        {
          if (layer.TryGetProperty("current", out var cur) && cur.ValueKind == JsonValueKind.Number)
            current = cur.GetInt64();
          if (total == 0 && layer.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
            total = size.GetInt64();
        }

        var status = el.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
            ? ty.GetString()
            : el.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() : null;

        return new ModelPullProgress { Status = status, Current = current, Total = total };
      }
      catch (JsonException)
      {
        return null;
      }
    }

    private static ModelInfo MapModel(JsonElement el)
    {
      var tags = el.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
          ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList()
          : new List<string>();

      var config = new Dictionary<string, string>();
      string format = null, arch = null, parameters = null, quant = null;
      long size = 0;

      if (el.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in cfg.EnumerateObject())
        {
          var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
          config[prop.Name] = value;
          switch (prop.Name.ToLowerInvariant())
          {
            case "format":
              format = value;
              break;
            case "architecture":
              arch = value;
              break;
            case "parameters":
              parameters = value;
              break;
            case "quantization":
              quant = value;
              break;
            case "size":
              size = ParseSize(value);
              break;
          }
        }
      }

      var created = el.TryGetProperty("created", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0L;

      return new ModelInfo
      {
        Id = el.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
        Reference = ReferenceFromTags(tags),
        Tags = tags,
        Format = format,
        Architecture = arch,
        ParameterCount = parameters,
        Quantization = quant,
        Size = size,
        Created = created > 0 ? DateTimeOffset.FromUnixTimeSeconds(created).UtcDateTime : default,
        Config = config
      };
    }

    private static ModelReference ReferenceFromTags(IReadOnlyList<string> tags)
    {
      if (tags == null || tags.Count == 0)
        return null;

      var tag = tags[0];
      const string hubPrefix = "docker.io/";
      if (tag.StartsWith(hubPrefix, StringComparison.OrdinalIgnoreCase))
        tag = tag[hubPrefix.Length..];

      return ModelReference.TryParse(tag, out var reference) ? reference : null;
    }

    private static IEnumerable<string[]> DataRows(string text, string headerToken)
    {
      var lines = (text ?? string.Empty).Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
      var headerSeen = false;
      foreach (var raw in lines)
      {
        var line = raw.TrimEnd();
        if (line.Length == 0)
          continue;

        if (!headerSeen)
        {
          if (line.Contains(headerToken, StringComparison.OrdinalIgnoreCase))
            headerSeen = true;
          continue;
        }

        var fields = MultiSpace.Split(line.Trim());
        if (fields.Length > 0 && fields[0].Length > 0)
          yield return fields;
      }
    }

    /// <summary>Parses a human-readable size (<c>256.35 MiB</c>, <c>270.60MB</c>, <c>103.56kB</c>) into bytes.</summary>
    private static long ParseSize(string value)
    {
      if (string.IsNullOrWhiteSpace(value))
        return 0;

      var match = SizeRegex.Match(value.Trim());
      if (!match.Success)
        return 0;

      if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        return 0;

      var letter = match.Groups[2].Value.ToUpperInvariant();
      var binary = match.Groups[3].Value.Length > 0;
      var @base = binary ? 1024d : 1000d;
      var exponent = letter switch
      {
        "K" => 1,
        "M" => 2,
        "G" => 3,
        "T" => 4,
        "P" => 5,
        _ => 0
      };

      return (long)(number * Math.Pow(@base, exponent));
    }
  }
}
