#nullable enable
namespace FluentDocker.Model.Drivers
{
  public static partial class ErrorCodes
  {
    /// <summary>
    /// Pod-related error codes (Podman-specific)
    /// </summary>
    public static class Pod
    {
      /// <summary>
      /// No pod matches the given name.
      /// </summary>
      public const string NotFound = "POD_001";

      /// <summary>
      /// The pod create operation failed.
      /// </summary>
      public const string CreateFailed = "POD_002";

      /// <summary>
      /// The pod remove operation failed.
      /// </summary>
      public const string RemoveFailed = "POD_003";

      /// <summary>
      /// The pod start operation failed.
      /// </summary>
      public const string StartFailed = "POD_004";

      /// <summary>
      /// The pod stop operation failed.
      /// </summary>
      public const string StopFailed = "POD_005";

      /// <summary>
      /// The pod restart operation failed.
      /// </summary>
      public const string RestartFailed = "POD_006";

      /// <summary>
      /// The pod kill operation failed.
      /// </summary>
      public const string KillFailed = "POD_007";

      /// <summary>
      /// The pod pause operation failed.
      /// </summary>
      public const string PauseFailed = "POD_008";

      /// <summary>
      /// The pod unpause operation failed.
      /// </summary>
      public const string UnpauseFailed = "POD_009";

      /// <summary>
      /// The pod inspect operation failed.
      /// </summary>
      public const string InspectFailed = "POD_010";

      /// <summary>
      /// Listing pods failed.
      /// </summary>
      public const string ListFailed = "POD_011";
    }
  }
}
