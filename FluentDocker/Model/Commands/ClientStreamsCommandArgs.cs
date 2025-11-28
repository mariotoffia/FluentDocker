using System;
using System.Text;
using FluentDocker.Extensions;
using FluentDocker.Model.Containers;

namespace FluentDocker.Model.Commands
{
  /// <summary>
  /// Arguments for docker logs command (streaming).
  /// </summary>
  public struct LogsCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Show extra details provided to logs.</summary>
    public bool Details { get; set; }
    /// <summary>Follow log output.</summary>
    public bool Follow { get; set; }
    /// <summary>Show logs since timestamp (e.g. 2013-01-02T13:23:37Z) or relative (e.g. 42m).</summary>
    public DateTime? Since { get; set; }
    /// <summary>Show logs before a timestamp or relative.</summary>
    public DateTime? Until { get; set; }
    /// <summary>Number of lines to show from the end of the logs.</summary>
    public int? Tail { get; set; }
    /// <summary>Show timestamps.</summary>
    public bool Timestamps { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Details)
        sb.Append(" --details");
      if (Follow)
        sb.Append(" -f");
      if (Since.HasValue)
        sb.Append($" --since {Since.Value:O}");
      if (Until.HasValue)
        sb.Append($" --until {Until.Value:O}");
      if (Tail.HasValue)
        sb.Append($" --tail={Tail.Value}");
      else
        sb.Append(" --tail=all");
      if (Timestamps)
        sb.Append(" -t");

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker events command (streaming).
  /// </summary>
  public struct EventsCommandArgs
  {
    /// <summary>Filter output based on conditions provided.</summary>
    public string[] Filters { get; set; }
    /// <summary>Show events created since timestamp.</summary>
    public DateTime? Since { get; set; }
    /// <summary>Stream events until this timestamp.</summary>
    public DateTime? Until { get; set; }
    /// <summary>Format the output using the given Go template.</summary>
    public string Format { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Since.HasValue)
        sb.Append($" --since {Since.Value:O}");
      if (Until.HasValue)
        sb.Append($" --until {Until.Value:O}");
      sb.OptionIfExists("--filter=", Filters);
      sb.OptionIfExists("--format ", Format);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker attach command (streaming).
  /// </summary>
  public struct AttachStreamCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>Override the key sequence for detaching.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Do not attach STDIN.</summary>
    public bool NoStdin { get; set; }
    /// <summary>Proxy all received signals to the process.</summary>
    public bool SigProxy { get; set; }
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

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker exec command (streaming).
  /// </summary>
  public struct ExecStreamCommandArgs
  {
    /// <summary>The container ID or name.</summary>
    public string ContainerId { get; set; }
    /// <summary>The command to execute.</summary>
    public string Command { get; set; }
    /// <summary>Arguments to the command.</summary>
    public string[] Arguments { get; set; }
    /// <summary>Keep STDIN open even if not attached.</summary>
    public bool Interactive { get; set; }
    /// <summary>Allocate a pseudo-TTY.</summary>
    public bool Tty { get; set; }
    /// <summary>Detached mode: run command in the background.</summary>
    public bool Detach { get; set; }
    /// <summary>Username or UID.</summary>
    public string User { get; set; }
    /// <summary>Working directory inside the container.</summary>
    public string WorkDir { get; set; }
    /// <summary>Set environment variables.</summary>
    public string[] Environment { get; set; }
    /// <summary>Give extended privileges to the command.</summary>
    public bool Privileged { get; set; }
    /// <summary>Override the key sequence for detaching a container.</summary>
    public string DetachKeys { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (Interactive)
        sb.Append(" -i");
      if (Tty)
        sb.Append(" -t");
      if (Detach)
        sb.Append(" -d");
      sb.OptionIfExists("-u ", User);
      sb.OptionIfExists("-w ", WorkDir);
      sb.OptionIfExists("-e ", Environment);
      if (Privileged)
        sb.Append(" --privileged");
      sb.OptionIfExists("--detach-keys ", DetachKeys);

      return sb.ToString();
    }
  }

  /// <summary>
  /// Arguments for docker stats command (streaming).
  /// </summary>
  public struct StatsStreamCommandArgs
  {
    /// <summary>The container IDs or names (empty for all containers).</summary>
    public string[] ContainerIds { get; set; }
    /// <summary>Show all containers (default shows just running).</summary>
    public bool All { get; set; }
    /// <summary>Format the output using a Go template.</summary>
    public string Format { get; set; }
    /// <summary>Do not truncate output.</summary>
    public bool NoTrunc { get; set; }
    /// <summary>Certificate paths for TLS.</summary>
    public ICertificatePaths Certificates { get; set; }

    public override string ToString()
    {
      var sb = new StringBuilder();

      if (All)
        sb.Append(" --all");
      sb.OptionIfExists("--format ", Format);
      if (NoTrunc)
        sb.Append(" --no-trunc");

      return sb.ToString();
    }
  }
}

