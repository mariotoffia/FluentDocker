using System;
using System.Collections.Generic;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker compose logs command (streaming).
  /// </summary>
  public struct ComposeLogsCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Services to show logs for.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Follow log output.</summary>
    public bool Follow { get; set; }
    /// <summary>Show timestamps.</summary>
    public bool Timestamps { get; set; }
    /// <summary>Show logs since timestamp (e.g. 2013-01-02T13:23:37Z) or relative (e.g. 42m).</summary>
    public DateTime? Since { get; set; }
    /// <summary>Show logs before a timestamp or relative.</summary>
    public DateTime? Until { get; set; }
    /// <summary>Number of lines to show from the end of the logs.</summary>
    public int? Tail { get; set; }
    /// <summary>Produce monochrome output.</summary>
    public bool NoColor { get; set; }
    /// <summary>Don't print prefix in logs.</summary>
    public bool NoLogPrefix { get; set; }
    /// <summary>Environment variables.</summary>
    public IDictionary<string, string> Env { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Follow)
        sb.Append(" -f");
      if (Timestamps)
        sb.Append(" -t");
      if (Since.HasValue)
        sb.Append($" --since {Since.Value:O}");
      if (Until.HasValue)
        sb.Append($" --until {Until.Value:O}");
      if (Tail.HasValue)
        sb.Append($" --tail={Tail.Value}");
      else
        sb.Append(" --tail=all");
      if (NoColor)
        sb.Append(" --no-color");
      if (NoLogPrefix)
        sb.Append(" --no-log-prefix");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose events command (streaming).
  /// </summary>
  public struct ComposeEventsCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Services to show events for.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Output events as a stream of JSON objects.</summary>
    public bool Json { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Json)
        sb.Append(" --json");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose watch command (streaming).
  /// </summary>
  public struct ComposeWatchCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Services to watch.</summary>
    public IList<string> Services { get; set; }
    /// <summary>Don't print prefix in logs.</summary>
    public bool NoUp { get; set; }
    /// <summary>Suppress verbose output.</summary>
    public bool Quiet { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (NoUp)
        sb.Append(" --no-up");
      if (Quiet)
        sb.Append(" --quiet");

      if (Services != null && Services.Count > 0)
        sb.Append(" " + string.Join(" ", Services));

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker compose attach command (streaming).
  /// </summary>
  public struct ComposeAttachCommandArgs
  {
    /// <summary>Alternative project name.</summary>
    public string AltProjectName { get; set; }
    /// <summary>Compose file paths.</summary>
    public IList<string> ComposeFiles { get; set; }
    /// <summary>Service name.</summary>
    public string Service { get; set; }
    /// <summary>Override the key sequence for detaching.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Do not attach STDIN.</summary>
    public bool NoStdin { get; set; }
    /// <summary>Proxy all received signals to the process.</summary>
    public bool SigProxy { get; set; }
    /// <summary>Index of the container if there are multiple instances.</summary>
    public int? Index { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      sb.OptionIfExists("--detach-keys ", DetachKeys);
      if (NoStdin)
        sb.Append(" --no-stdin");
      if (SigProxy)
        sb.Append(" --sig-proxy");
      if (Index.HasValue)
        sb.Append($" --index={Index.Value}");

      return sb.ToString();
    }
  }
}

