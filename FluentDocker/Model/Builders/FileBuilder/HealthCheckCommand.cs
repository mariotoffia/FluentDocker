#nullable enable
using System.Globalization;
using System.Text;
using FluentDocker.Extensions;

namespace FluentDocker.Model.Builders.FileBuilder
{
  /// <summary>
  /// Represents a Dockerfile <c>HEALTHCHECK</c> instruction.
  /// </summary>
  /// <param name="cmd">The command with it's argument to do when performing the health check.</param>
  /// <param name="interval">Optional (default is 30s) interval when to invoke the <paramref name="cmd"/>.</param>
  /// <param name="timeout">Optional (default is 30s) when the healthcheck is force cancelled and failed.</param>
  /// <param name="startPeriod">Optional (default is 0s) when it shall start to execute the <paramref name="cmd"/>.</param>
  /// <param name="retries">
  /// Optional (default is 3) number of retries before considering the container unhealthy.
  /// <c>0</c> means "not specified": no <c>--retries</c> flag is emitted and Docker's own
  /// default (3) applies — <c>docker build</c> rejects a literal <c>--retries=0</c>.
  /// </param>
  /// <remarks>
  ///   A <paramref name="cmd"/> can be e.g. a curl command combined by other shell command for example:
  ///   "curl -f http://localhost/ || exit 1".
  /// </remarks>
  /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="retries"/> is negative.</exception>
  public sealed class HealthCheckCommand(string cmd, string? interval = null, string? timeout = null, string? startPeriod = null, int retries = 3) : ICommand
  {
    /// <summary>Gets the health check command.</summary>
    public string Cmd { get; } = DockerfileInstructionGuard.Require(
        cmd, "HEALTHCHECK", "command", "HEALTHCHECK requires a command.");
    /// <summary>Gets the check interval.</summary>
    public string Interval { get; } = DockerfileInstructionGuard.Optional(
        interval, "HEALTHCHECK", "interval", "30s");
    /// <summary>Gets the check timeout.</summary>
    public string Timeout { get; } = DockerfileInstructionGuard.Optional(
        timeout, "HEALTHCHECK", "timeout", "30s");
    /// <summary>Gets the start period.</summary>
    public string StartPeriod { get; } = DockerfileInstructionGuard.Optional(
        startPeriod, "HEALTHCHECK", "start period", "0s");
    /// <summary>Gets the unhealthy retry count.</summary>
    public int Retries { get; } = retries >= 0
        ? retries
        : throw new System.ArgumentOutOfRangeException(
            nameof(retries), retries, "HEALTHCHECK retries cannot be negative.");

    /// <summary>Renders the instruction.</summary>
    public override string ToString()
    {
      // No trailing space after the keyword: every OptionIfExists/Append below emits its own
      // leading space, so a trailing one here would render "HEALTHCHECK  --interval=…".
      var sb = new StringBuilder();
      sb.Append("HEALTHCHECK");
      sb.OptionIfExists("--interval=", Interval);
      sb.OptionIfExists("--timeout=", Timeout);
      sb.OptionIfExists("--start-period=", StartPeriod);

      // 0 = "not specified" (docker rejects a literal --retries=0); 3 is docker's own
      // default, so emitting it would be redundant.
      if (Retries != 3 && Retries != 0)
      {
        sb.Append(CultureInfo.InvariantCulture, $" --retries={Retries}");
      }

      sb.Append(CultureInfo.InvariantCulture, $" CMD {Cmd}");

      return sb.ToString();
    }
  }
}
