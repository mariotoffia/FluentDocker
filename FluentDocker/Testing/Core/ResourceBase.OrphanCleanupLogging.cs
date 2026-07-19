#nullable disable warnings
using System;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  public abstract partial class ResourceBase
  {
    private static readonly Action<ILogger, string, Exception> OrphanCleanupFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(7, nameof(OrphanCleanupFailed)),
            "Orphan cleanup failed: {Error}");

    private static readonly Action<ILogger, string, Exception> OrphanCleanupReportedErrors =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(8, nameof(OrphanCleanupReportedErrors)),
            "Orphan cleanup completed with errors: {Errors}");

    private void LogOrphanCleanupFailure(Exception ex)
    {
      OrphanCleanupFailed(Logger, ex.Message, ex);
    }

    private void LogOrphanCleanupErrors(OrphanCleanup.CleanupResult cleanup)
    {
      if (cleanup.Errors.Count == 0)
        return;
      OrphanCleanupReportedErrors(Logger, string.Join("; ", cleanup.Errors), null);
    }
  }
}
