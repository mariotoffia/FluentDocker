using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  internal static class DockerCliJsonLineParser
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];

    public static bool TryParse<T>(
        string output,
        ILogger logger,
        string logMessage,
        out List<T> items,
        out string error)
    {
      items = [];
      error = null;
      Exception firstException = null;
      var failureCount = 0;
      var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

      foreach (var line in lines)
      {
        try
        {
          var item = JsonSerializer.Deserialize<T>(line, JsonHelper.CaseInsensitiveOptions);
          if (item != null)
            items.Add(item);
        }
        catch (Exception ex)
        {
          firstException ??= ex;
          failureCount++;
          logger.LogError(ex, "{DockerCliJsonParseMessage}", logMessage);
        }
      }

      if (lines.Length > 0 && failureCount == lines.Length && items.Count == 0)
      {
        // Docker CLI formats can drift; all-failed parse must not become Ok([]).
        error = $"Failed to parse Docker CLI JSON output: {firstException?.Message}";
        return false;
      }

      return true;
    }
  }
}
