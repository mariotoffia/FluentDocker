#nullable enable
using System;

namespace FluentDocker.Common
{
  /// <summary>
  /// Thrown when a driver context carries a different driver ID than the registration ID.
  /// </summary>
  /// <remarks>
  /// This remains an <see cref="ArgumentException"/> so callers can inspect
  /// <see cref="ArgumentException.ParamName"/> for the invalid registration argument.
  /// </remarks>
  public sealed class DriverContextIdMismatchException : ArgumentException
  {
    /// <summary>
    /// Driver ID found in the supplied context.
    /// </summary>
    public string ContextDriverId { get; }

    /// <summary>
    /// Driver ID used for registration.
    /// </summary>
    public string RegistrationDriverId { get; }

    /// <summary>
    /// Initializes a new instance for a mismatched driver context.
    /// </summary>
    public DriverContextIdMismatchException(
        string contextDriverId,
        string registrationDriverId,
        string paramName)
        : base(
            $"Driver context ID '{contextDriverId}' does not match registration ID '{registrationDriverId}'.",
            paramName)
    {
      ContextDriverId = contextDriverId;
      RegistrationDriverId = registrationDriverId;
    }
  }
}
