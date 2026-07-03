using System;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Exception thrown when a test resource fails to initialize after diagnostics
  /// have been collected.
  /// </summary>
  public sealed class ResourceInitializationException : Exception
  {
    /// <summary>
    /// Diagnostics captured before the failed resource was cleaned up.
    /// </summary>
    public ResourceDiagnostics? Diagnostics { get; }

    /// <summary>
    /// Creates an initialization exception with diagnostics and the original
    /// failure as <see cref="Exception.InnerException"/>.
    /// </summary>
    public ResourceInitializationException(
        string message,
        ResourceDiagnostics? diagnostics,
        Exception innerException)
        : base(message, innerException)
        => Diagnostics = diagnostics;
  }
}
