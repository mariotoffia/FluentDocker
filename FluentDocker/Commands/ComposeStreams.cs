using System;
using System.Threading;
using FluentDocker.Common;
using FluentDocker.Executors;
using FluentDocker.Executors.Mappers;
using FluentDocker.Extensions;
using FluentDocker.Extensions.Utils;
using FluentDocker.Model.Common;
using FluentDocker.Model.Compose;
using FluentDocker.Model.Containers;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker Compose streaming commands (logs, events).
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Use the IStreamDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use IStreamDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class ComposeStreams
  {
    /// <summary>
    /// Returns the appropriate binary and command string for Docker Compose operations,
    /// handling both V1 and V2 formats.
    /// </summary>
    private static (string binary, string command) GetComposeCommand(ComposeVersion version = ComposeVersion.Unknown)
    {
      var resolver = new DockerBinariesResolver(SudoMechanism.None, null);
      var isV2 = resolver.IsDockerComposeV2Available;

      if (isV2)
      {
        if (version != ComposeVersion.Unknown && version != ComposeVersion.V2)
        {
          throw new FluentDockerException(
            $"Requested compose version {version} but only V2 is available. Use the overload that accepts ComposeVersion to specify the version.");
        }

        // For V2, we resolve 'docker' and add 'compose' as the first command
        return ("docker".ResolveBinary(), "compose");
      }
      else
      {
        if (version != ComposeVersion.Unknown && version != ComposeVersion.V1)
        {
          throw new FluentDockerException(
            $"Requested compose version {version} but only V1 is available. Use the overload that accepts ComposeVersion to specify the version.");
        }

        // For V1, we use the traditional docker-compose binary
        return ("docker-compose".ResolveBinary(), "");
      }
    }

    public static ConsoleStream<string> ComposeLogs(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/,
      CancellationToken cancellationToken = default(CancellationToken),
      bool follow = false, bool showTimeStamps = false, DateTime? since = null, int? numLines = null, bool noColor = false,
      ICertificatePaths certificates = null)
    {
      var (binary, command) = GetComposeCommand();
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (follow)
      {
        options += " -f";
      }

      if (null != since)
      {
        options += $" --since {since}";
      }

      options += numLines.HasValue ? $" --tail={numLines}" : " --tail=all";

      if (showTimeStamps)
      {
        options += " -t";
      }

      if (noColor)
      {
        options += " --no-color";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      return
        new StreamProcessExecutor<StringMapper, string>(
          binary,
          $"{args} {(string.IsNullOrEmpty(command) ? "" : command + " ")}logs {options}").Execute(cancellationToken);
    }

    public static ConsoleStream<string> ComposeEvents(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/,
      CancellationToken cancellationToken = default(CancellationToken),
      bool json = false,
      ICertificatePaths certificates = null)
    {
      var (binary, command) = GetComposeCommand();
      var args = $"{host.RenderBaseArgs(certificates)}";

      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f \"{composeFile}\"";
      }

      if (!string.IsNullOrEmpty(altProjectName))
      {
        args += $" -p {altProjectName}";
      }

      var options = string.Empty;
      if (json)
      {
        options += " --json";
      }

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      return
        new StreamProcessExecutor<StringMapper, string>(
          binary,
          $"{args} {(string.IsNullOrEmpty(command) ? "" : command + " ")}events {options}").Execute(cancellationToken);
    }
  }
}
