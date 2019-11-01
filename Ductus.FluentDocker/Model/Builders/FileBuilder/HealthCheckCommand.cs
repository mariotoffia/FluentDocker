using Ductus.FluentDocker.Extensions;
using System.Text;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class HealthCheckCommand : ICommand
  {
    /// <summary>
    /// Creates an instance.
    /// </summary>
    /// <param name="cmd">The command with it's argument to do when performing the health check.</param>
    /// <param name="interval">Optional (default is 30s) interval when to invoke the <paramref name="cmd"/>.</param>
    /// <param name="timeout">Optional (default is 30s) when the healthcheck is force cancelled and failed.</param>
    /// <param name="startPeriod">Optional (default is 0s) when it shall start to excute the <paramref name="cmd"/>.</param>
    /// <param name="retries">Optional (default is 3) number retries before consider it as non healthy.</param>
    /// <remarks>
    ///   A <paramref name="cmd"/> can be e.g. a curl command combined by other shell command for example:
    ///   "curl -f http://localhost/ || exit 1".
    /// </remarks>
    public HealthCheckCommand(string cmd, string interval = null, string timeout = null, string startPeriod = null, int retries = 3)
    {
      Cmd = cmd;
      Interval = string.IsNullOrEmpty(interval) ? "30s" : interval;
      Timeout = string.IsNullOrEmpty(timeout) ? "30s" : timeout;
      StartPeriod = string.IsNullOrEmpty(startPeriod) ? "0s" : startPeriod;
      Retries = retries;
    }

    public string Cmd { get; }
    public string Interval { get; }
    public string Timeout { get; }
    public string StartPeriod { get; }
    public int Retries { get; }

    public override string ToString()
    {
      var sb = new StringBuilder();
      sb.Append("HEALTHCHECK ");
      sb.OptionIfExists("--interval=", Interval);
      sb.OptionIfExists("--timeout=", Timeout);
      sb.OptionIfExists("--start-period=", StartPeriod);

      if (Retries != 3)
      {
        sb.Append($" --retries={Retries}");
      }

      sb.Append($" CMD {Cmd}");

      return sb.ToString();
    }
  }
}
