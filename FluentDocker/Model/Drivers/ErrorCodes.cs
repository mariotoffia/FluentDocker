#nullable enable
namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Hierarchical error codes for FluentDocker v3.0.0.
  /// </summary>
  /// <remarks>
  /// Codes are grouped into nested static classes by domain (e.g. <see cref="Container"/>,
  /// <see cref="Image"/>, <see cref="Model"/>). Each group lives in its own
  /// <c>ErrorCodes.&lt;Group&gt;.cs</c> partial file so this type stays under the project's
  /// 500-line file-size limit; this file holds only the shared transient-classifier.
  /// </remarks>
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Returns true when the code represents a connection, timeout, or transient server
    /// failure that may succeed on retry.
    /// </summary>
    public static bool IsTransientCode(string errorCode)
    {
      // API 500 is intentionally excluded: daemon server errors can be deterministic, not retryable hiccups.
      return errorCode is General.Timeout
          or Network.Timeout
          or Api.ConnectionFailed
          or Api.StreamInterrupted
          // A stopped Podman machine is transient: start it and retry (POD-MAJ-3). Aligns
          // IsTransient with the machine-not-running exception marked transient at its throw site.
          or Machine.NotRunning
          or ModelInference.EndpointUnreachable
          or ModelInference.Timeout
          or ModelInference.ServiceUnavailable;
    }
  }
}
