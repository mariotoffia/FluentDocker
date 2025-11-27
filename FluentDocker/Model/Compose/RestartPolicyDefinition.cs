using System;

namespace Ductus.FluentDocker.Model.Compose
{
  /// <summary>
  /// Configures if and how to restart containers when they exit. Replaces
  /// <see cref="ComposeServiceDefinition.Restart"/>.
  /// </summary>
  public sealed class RestartPolicyDefinition
  {
    /// <summary>
    /// One of none, on-failure or any (default: any).
    /// </summary>
    public string Condition { get; set; } = "any";
    /// <summary>
    /// How long to wait between restart attempts, specified as a duration (default: 0).
    /// </summary>
    /// <remarks>
    /// For example delay: 5s
    /// </remarks>
    public string Delay { get; set; } = "0";
    /// <summary>
    /// How many times to attempt to restart a container before giving up (default: never give up).
    /// </summary>
    /// <remarks>
    ///  If the restart does not succeed within the configured window, this attempt doesn’t count toward the
    /// configured max_attempts value. For example, if max_attempts is set to ‘2’, and the restart fails on the first
    /// attempt, more than two restarts may be attempted.
    /// </remarks>
    public int MaxAttempts { get; set; } = int.MaxValue;
    /// <summary>
    /// How long to wait before deciding if a restart has succeeded.
    /// </summary>
    /// <remarks>
    /// This specified as a duration (default: decide immediately). For example window: 120s.
    /// </remarks>
    public string Window { get; set; }
  }
}
