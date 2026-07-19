using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Services.Impl;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Extensions
{
  /// <summary>
  /// Private helpers shared by the container wait extensions (retry classification, inspect-cache
  /// invalidation and diagnostic logging). Split from <see cref="ServiceExtensions"/> purely to keep
  /// each source file within the repository's 500-line limit (SVC-6); behavior is identical.
  /// </summary>
  public static partial class ServiceExtensions
  {
    // Whitelist of genuinely transient probe failures. DriverException/OperationCanceledException
    // are already handled by dedicated catch clauses at each call site before this one runs, so in
    // practice a DriverException reaching here is never transient (transient ones were already
    // caught by the dedicated clause) and a TaskCanceledException reaching here is always a
    // per-attempt timeout, not caller cancellation (that's caught by the dedicated OCE clause
    // first). Everything else - programming errors, serialization failures, driver-invariant
    // violations - propagates instead of being silently retried to a timeout.
    private static bool IsRetriableWaitException(Exception ex, CancellationToken cancellationToken) =>
        ex switch
        {
          SocketException => true,
          HttpRequestException => true,
          DriverException { IsTransient: true } => true,
          TaskCanceledException => !cancellationToken.IsCancellationRequested,
          _ => false
        };

    private static void InvalidateInspectCache(IContainerService service)
    {
      if (service is ContainerService containerService)
        containerService.InvalidateInspectCache();
    }

    private static void LogDebug(IContainerService service, Exception exception, string operation, string value)
    {
      if (service is not ContainerService containerService)
        return;

      var logger = containerService.Kernel.LoggerFactory.CreateLogger(typeof(ServiceExtensions).FullName!);
      if (logger.IsEnabled(LogLevel.Debug))
      {
        logger.LogDebug(
            exception,
            "Container wait helper poll failed during {Operation} for {Value}",
            operation,
            value);
      }
    }

    private static void LogWaitFailure(IContainerService service, Exception exception, string operation, string value)
    {
      if (exception == null || service is not ContainerService containerService)
        return;

      containerService.Kernel.LoggerFactory.CreateLogger(typeof(ServiceExtensions).FullName!)
          .LogWarning(
              exception,
              "Container wait helper timed out during {Operation} for {Value}; last failure is attached",
              operation,
              value);
    }
  }
}
