using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Builders
{
  internal sealed partial class ComposeBuilder
  {
    private void Validate()
    {
      if (_projectName != null && !IsValidProjectName(_projectName))
      {
        throw new FluentDocker.Common.FluentDockerException(
            $"Invalid compose project name '{_projectName}'. Expected [a-z0-9][a-z0-9_-]*.");
      }
    }

    private static bool IsValidProjectName(string name)
    {
      if (string.IsNullOrEmpty(name) || !IsLowerAlphaNumeric(name[0]))
        return false;
      for (var i = 1; i < name.Length; i++)
      {
        var c = name[i];
        if (!IsLowerAlphaNumeric(c) && c != '_' && c != '-')
          return false;
      }
      return true;
    }

    private static bool IsLowerAlphaNumeric(char c) =>
        c is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static async Task<bool> ComposeProjectExistsAsync(
        Drivers.IComposeDriver driver,
        DriverContext context,
        Drivers.ComposeUpConfig upConfig,
        CancellationToken cancellationToken)
    {
      try
      {
        var probe = driver.ListAsync(context, new Drivers.ComposeListConfig
        {
          ComposeFiles = upConfig.ComposeFiles,
          ProjectName = upConfig.ProjectName,
          Environment = upConfig.Environment,
          All = true
        }, cancellationToken);
        // Fail safe: never `down` a project we cannot prove we created.
        if (probe == null)
          return true;
        var response = await probe.ConfigureAwait(false);
        if (!response.Success)
          return true;
        return response.Data?.Count > 0;
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch
      {
        return true;
      }
    }

    private static string StripEnvValueQuotes(string value)
    {
      if (value.Length >= 2 &&
          ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        return value[1..^1];
      return value;
    }

    private static string StripUnquotedInlineComment(string value)
    {
      var quote = '\0';
      for (var i = 0; i < value.Length; i++)
      {
        var c = value[i];
        if ((c == '"' || c == '\'') && quote == '\0')
          quote = c;
        else if (c == quote && !(quote == '"' && IsEscaped(value, i)))
          quote = '\0';
        else if (c == '#' && quote == '\0' && i > 0 && char.IsWhiteSpace(value[i - 1]))
          return value[..i].TrimEnd();
      }
      return value;
    }

    private static bool IsEscaped(string value, int index)
    {
      var backslashes = 0;
      for (var i = index - 1; i >= 0 && value[i] == '\\'; i--)
        backslashes++;
      return backslashes % 2 == 1;
    }

    private static async Task CleanupFailedComposeAsync(
        Drivers.IComposeDriver driver,
        DriverContext context,
        Drivers.ComposeUpConfig upConfig,
        bool removeVolumes,
        bool borrowedProject,
        TimeSpan cleanupTimeout)
    {
      if (borrowedProject)
        return;

      try
      {
        using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
        await driver.DownAsync(context, new Drivers.ComposeDownConfig
        {
          ComposeFiles = upConfig.ComposeFiles,
          ProjectName = upConfig.ProjectName,
          Environment = upConfig.Environment,
          RemoveVolumes = removeVolumes
        }, cleanupCts.Token).WaitAsync(cleanupCts.Token).ConfigureAwait(false);
      }
      catch
      {
        // Best effort only; preserve the original compose-up failure.
      }
    }
  }
}
