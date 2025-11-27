using System;
using System.Threading;
using Ductus.FluentDocker.Commands;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Executors;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Events;

namespace Ductus.FluentDocker.Services.Extensions
{
  public static class HostExtensions
  {
    /// <summary>
    ///   Read the events from the event stream.
    /// </summary>
    /// <param name="host">The host to attach to when listening for its events</param>
    /// <param name="token">The cancellation token for logs, especially needed when <paramref name="follow" /> is set to true.</param>
    /// <param name="filters">A optional set of filters to narrow the amount of events.</param>
    /// <param name="since">A optional since filter.</param>
    /// <param name="until">A optional stop reading events.</param>
    /// <returns>A console stream to consume the incoming <see cref="FdEvent"/>s.</returns>
    public static ConsoleStream<FdEvent> Events(this IHostService host, CancellationToken token = default,
      string[] filters = null, DateTime? since = null, DateTime? until = null)
    {
      return host.Host.FdEvents(token, filters, since, until, host.Certificates);
    }

    public static Result<string> Build(this IHostService host, string name, string tag, string workdir = null,
      ContainerBuildParams prms = null)
    {
      var res = host.Host.Build(name, tag, workdir, prms, host.Certificates);
      return res.Success ? res.Data[0].ToSuccess() : string.Empty.ToFailure(res.Error, res.Log);
    }
  }
}
