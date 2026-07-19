using System;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Disposal/teardown <see cref="LoggerMessage"/> definitions for <see cref="ResourceBase"/>.
  /// Split out purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public abstract partial class ResourceBase
  {
    private static readonly Action<ILogger, Exception> GracefulAndForceRemoveFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(GracefulAndForceRemoveFailed)),
            "Graceful teardown failed and force-remove also failed; resource remains provisioned for retry.");
    private static readonly Action<ILogger, Exception> ForceRemoveFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(ForceRemoveFailed)),
            "Force-remove failed after graceful teardown failure.");
    private static readonly Action<ILogger, Exception> GracefulTeardownRecovered =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, nameof(GracefulTeardownRecovered)),
            "Graceful teardown failed; force-remove succeeded and cleaned up the resource.");
    private static readonly Action<ILogger, Exception> BeforeDisposeHookFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4, nameof(BeforeDisposeHookFailed)),
            "Before-dispose hook failed.");
    private static readonly Action<ILogger, Exception> AfterDisposeHookFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(5, nameof(AfterDisposeHookFailed)),
            "After-dispose hook failed.");
  }
}
