using System;
using System.Collections.Generic;
using System.Globalization;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// OrphanCleanup partial: session/age/label predicates shared by the per-resource-kind sweeps, plus
  /// the "abandoned late provision" bookkeeping used when a resource is created and torn down within
  /// the same process run (so it never gets a chance to be seen as belonging to the current session).
  /// Split out purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public static partial class OrphanCleanup
  {
    private static bool IsCurrentSession(
        IDictionary<string, string> labels, string currentSessionId)
    {
      if (currentSessionId == null)
        return IsSession(labels, SessionLabel.SharedSessionId());

      return IsSession(labels, currentSessionId) ||
             IsSession(labels, SessionLabel.SharedSessionId());
    }

    private static bool IsSession(IDictionary<string, string> labels, string sessionId)
    {
      if (labels == null || string.IsNullOrWhiteSpace(sessionId))
        return false;
      return labels.TryGetValue(SessionLabel.Key, out var resourceSessionId)
             && resourceSessionId == sessionId;
    }

    internal static void MarkAbandonedLateProvision(string resourceName, string sessionId)
    {
      if (!string.IsNullOrWhiteSpace(resourceName) &&
          !string.IsNullOrWhiteSpace(sessionId))
        AbandonedLateProvisionNames.TryAdd(
            AbandonedLateProvisionKey(resourceName, sessionId), 0);
    }

    private static bool IsAbandonedLateProvision(
        IDictionary<string, string> labels,
        params string[] names)
    {
      if (labels == null ||
          !labels.TryGetValue(SessionLabel.Key, out var sessionId) ||
          string.IsNullOrWhiteSpace(sessionId))
        return false;

      foreach (var name in names)
      {
        if (!string.IsNullOrWhiteSpace(name) &&
            AbandonedLateProvisionNames.ContainsKey(
                AbandonedLateProvisionKey(name, sessionId)))
          return true;
      }

      return false;
    }

    private static void ClearAbandonedLateProvision(
        IDictionary<string, string> labels,
        params string[] names)
    {
      if (labels == null ||
          !labels.TryGetValue(SessionLabel.Key, out var sessionId) ||
          string.IsNullOrWhiteSpace(sessionId))
        return;

      foreach (var name in names)
      {
        if (!string.IsNullOrWhiteSpace(name))
          AbandonedLateProvisionNames.TryRemove(
              AbandonedLateProvisionKey(name, sessionId), out _);
      }
    }

    private static string AbandonedLateProvisionKey(string name, string sessionId) =>
        $"{sessionId}\u001f{NormalizeResourceName(name)}";

    private static string NormalizeResourceName(string name) =>
        name.Trim().TrimStart('/');

    private static bool ShouldPreserveDueToAge(
        IDictionary<string, string> labels,
        TimeSpan minimumAge,
        DateTimeOffset daemonCreated = default)
    {
      if (minimumAge <= TimeSpan.Zero)
        return false;

      if (daemonCreated != default)
        return daemonCreated.ToUniversalTime() > DateTimeOffset.UtcNow - minimumAge;

      if (labels == null ||
          !labels.TryGetValue(SessionLabel.CreatedAtKey, out var createdAt) ||
          !DateTimeOffset.TryParse(
              createdAt,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out var created))
        return true;

      return created.ToUniversalTime() > DateTimeOffset.UtcNow - minimumAge;
    }

    private static bool ShouldReapRunning(IDictionary<string, string> labels, DateTimeOffset created) =>
        RunningReapCeiling() is { } ceiling && ceiling > TimeSpan.Zero && !ShouldPreserveDueToAge(labels, ceiling, created);
    private static TimeSpan? RunningReapCeiling() => ParseDuration(Environment.GetEnvironmentVariable(SessionLabel.ReapRunningAfterEnvironmentVariable));
    private static TimeSpan? ParseDuration(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
        return null;
      raw = raw.Trim();
      var unit = char.ToLowerInvariant(raw[raw.Length - 1]);
      var perUnit = unit switch { 'd' => TimeSpan.FromDays(1), 'h' => TimeSpan.FromHours(1), 'm' => TimeSpan.FromMinutes(1), 's' => TimeSpan.FromSeconds(1), _ => TimeSpan.Zero };
      var number = perUnit == TimeSpan.Zero ? raw : raw.Substring(0, raw.Length - 1);
      if (perUnit == TimeSpan.Zero)
        perUnit = TimeSpan.FromHours(1);
      return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && value < TimeSpan.MaxValue / perUnit ? perUnit * value : null;
    }

    private static bool RemoveSucceeded(
        CommandResponse<Unit> response,
        string type,
        string name,
        CleanupResult result)
    {
      if (response?.Success == true)
        return true;
      result.Errors.Add($"Failed to remove {type} {name}: {response?.Error ?? "unknown error"}");
      return false;
    }
  }
}
