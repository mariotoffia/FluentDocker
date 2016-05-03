using System;
using System.Threading;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Mappers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class ComposeStreams
  {
    public static ConsoleStream<string> ComposeLogs(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/,
      CancellationToken cancellationToken = default(CancellationToken),
      bool follow = false, bool showTimeStamps = false, DateTime? since = null, int? numLines = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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

      if (null != services && 0 != services.Length)
      {
        options += " " + string.Join(" ", services);
      }

      return
        new StreamProcessExecutor<StringMapper, string>(
          "docker-compose".ResolveBinary(),
          $"{args} logs {options}").Execute(cancellationToken);
    }

    public static ConsoleStream<string> ComposeEvents(this DockerUri host, string altProjectName = null,
      string composeFile = null, string[] services = null /*all*/,
      CancellationToken cancellationToken = default(CancellationToken),
	  bool json = false,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";
      if (!string.IsNullOrEmpty(composeFile))
      {
        args += $" -f {composeFile}";
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
          "docker-compose".ResolveBinary(),
          $"{args} events {options}").Execute(cancellationToken);
    }
  }
}