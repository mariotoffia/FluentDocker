using System;
using System.Threading;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Executors.Mappers;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Commands
{
  public static class ClientStreams
  {
    public static ConsoleStream<string> Logs(this DockerUri host, string id,
      CancellationToken cancellationToken = default(CancellationToken),
      bool follow = false, bool showTimeStamps = false, DateTime? since = null, int? numLines = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

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

      return
        new StreamProcessExecutor<StringMapper, string>(
          "docker".ResolveBinary(),
          $"{args} logs {options} {id}").Execute(cancellationToken);
    }

    public static ConsoleStream<string> Events(this DockerUri host,
      CancellationToken cancellationToken = default(CancellationToken),
      string []filters = null, DateTime ?since = null, DateTime? until = null,
      ICertificatePaths certificates = null)
    {
      var args = $"{host.RenderBaseArgs(certificates)}";

      var options = string.Empty;
      if (null != since)
      {
        options += $" --since {since}";
      }

      if (null != until)
      {
        options += $" --since {until}";
      }

      if (null != filters && 0 != filters.Length)
      {
        foreach (var filter in filters)
        {
          options += $" --filter={filter}";
        }
      }

      return
        new StreamProcessExecutor<StringMapper, string>(
          "docker".ResolveBinary(),
          $"{args} events {options}").Execute(cancellationToken);
    }
  }
}
